// An account partition hint is never an authentication credential. Public offline
// shells have no member attribute; authenticated online pages refresh the hint.
const key = 'qz.crossword.current-member';
const memberPattern = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function accountHint() {
    try { const member = localStorage.getItem(key); return memberPattern.test(member ?? '') ? member : null; }
    catch { return null; }
}
export function rememberAccount(member) {
    try {
        if (memberPattern.test(member ?? '')) localStorage.setItem(key, member);
        else localStorage.removeItem(key);
    } catch { /* Local storage can be unavailable; online session identity still wins. */ }
}
if (document.body.hasAttribute('data-crossword-member')) rememberAccount(document.body.dataset.crosswordMember);
