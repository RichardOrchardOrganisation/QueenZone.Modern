export const preloadMargin = 300;
export const retentionMargin = 600;
export const evictionDelayMs = 250;
export const maximumPlayers = 2;
export type CardGeometry = { top: number; bottom: number; width: number; viewportTop: number; viewportBottom: number };
export type PoolCard = {
  key: string; owner: number; generation: number | null; geometry: CardGeometry | null;
  playing: boolean; fullscreen: boolean; failed: string | null; evictionAt: number | null;
};
export type PoolSnapshot = Record<string, PoolCard>;
/** Screen-local resource coordinator, unrelated to API/cache invalidation. */
export class PlayerPool {
  cards: PoolSnapshot = {};
  enabled = false;
  private sequence = 0;
  private failures = new Map<string, string>();
  private changed: (snapshot: PoolSnapshot) => void;
  private pause: (key: string) => void;
  constructor(changed: (snapshot: PoolSnapshot) => void, pause: (key: string) => void) {
    this.changed = changed; this.pause = pause;
  }
  register(key: string): number {
    if (this.cards[key]) this.remove(key, this.cards[key].owner);
    const owner = ++this.sequence;
    this.cards[key] = { key, owner, generation: null, geometry: null, playing: false, fullscreen: false, failed: this.failures.get(key) ?? null, evictionAt: null };
    this.publish(); return owner;
  }
  remove(key: string, owner: number): void {
    if (this.cards[key]?.owner !== owner) return;
    this.release(this.cards[key]); delete this.cards[key]; this.reconcile();
  }
  setEnabled(enabled: boolean): void {
    this.enabled = enabled;
    if (!enabled) for (const c of Object.values(this.cards)) { this.release(c); c.geometry = null; }
    this.reconcile();
  }
  measure(key: string, owner: number, geometry: CardGeometry, now = Date.now(), defer = false): void {
    const c = this.cards[key];
    if (!c || c.owner !== owner) return;
    // Native fullscreen hides/reparents the WebView. Zero-sized measurements
    // are not evidence the card left the thread.
    if (geometry.width <= 0 || geometry.bottom <= geometry.top) return;
    c.geometry = geometry;
    if (!defer) this.reconcile(now);
  }
  message(key: string, generation: number, event: 'state' | 'fullscreen', value: number | boolean): void {
    const c = this.cards[key];
    if (!this.enabled || c?.generation !== generation) return;
    if (event === 'fullscreen') c.fullscreen = value === true;
    else {
      c.playing = value === 1;
      if (c.playing) for (const other of Object.values(this.cards)) {
        if (other.key !== key && other.generation != null) {
          this.pause(other.key); other.playing = false;
        }
      }
    }
    this.reconcile();
  }
  fail(key: string, generation: number, reason: string): void {
    const c = this.cards[key]; if (c?.generation !== generation) return;
    c.failed = reason; this.failures.set(key, reason); this.release(c); this.reconcile();
  }
  retry(key: string, owner: number): void {
    const c = this.cards[key]; if (c?.owner !== owner) return;
    c.failed = null; this.failures.delete(key); this.reconcile();
  }
  tick(now = Date.now()): void { this.reconcile(now); }
  private release(c: PoolCard): void {
    if (c.generation != null) this.pause(c.key);
    c.generation = null; c.playing = false; c.fullscreen = false; c.evictionAt = null;
  }
  private publish(): void { this.changed(Object.fromEntries(Object.entries(this.cards).map(([k, c]) => [k, { ...c }]))); }
  private reconcile(now = Date.now()): void {
    if (!this.enabled) { this.publish(); return; }
    const distance = (c: PoolCard) => {
      const g = c.geometry;
      return g ? Math.max(g.viewportTop - g.bottom, g.top - g.viewportBottom, 0) : Infinity;
    };
    const visible = (c: PoolCard) => distance(c) === 0;
    for (const c of Object.values(this.cards)) {
      if (c.generation == null || c.fullscreen) continue;
      if (distance(c) <= retentionMargin) c.evictionAt = null;
      else {
        c.evictionAt ??= now + evictionDelayMs;
        if (now >= c.evictionAt) this.release(c);
      }
    }
    const priority = (c: PoolCard) => c.fullscreen || (c.playing && visible(c)) ? 0 : c.evictionAt != null ? 1 : 2;
    const candidates = Object.values(this.cards).filter(c => !c.failed && c.geometry && c.geometry.width >= 200
      && (c.fullscreen || c.evictionAt != null || distance(c) <= (c.generation == null ? preloadMargin : retentionMargin)));
    candidates.sort((a, b) => priority(a) - priority(b) || distance(a) - distance(b)
      || Math.abs((a.geometry!.top + a.geometry!.bottom - a.geometry!.viewportTop - a.geometry!.viewportBottom) / 2)
        - Math.abs((b.geometry!.top + b.geometry!.bottom - b.geometry!.viewportTop - b.geometry!.viewportBottom) / 2)
      || a.owner - b.owner);
    const selected = new Set(candidates.slice(0, maximumPlayers).map(c => c.key));
    for (const c of Object.values(this.cards)) if (!selected.has(c.key)) this.release(c);
    for (const c of candidates.slice(0, maximumPlayers)) if (c.generation == null) c.generation = ++this.sequence;
    this.publish();
  }
}
