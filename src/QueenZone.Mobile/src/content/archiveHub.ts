import type { BadgeRole } from '../ui/Badge';
import type { ArchiveIconName } from '../ui/icons/archiveSectionIcons';

/** Spec §4.3b Archive hub destinations, plus Trivia (#1101). */
export const ARCHIVE_HUB_IDS = [
  'stories',
  'timeline',
  'biography',
  'discography',
  'tribute',
  'fan-performances',
  'recently-restored',
  'trivia',
  'about',
] as const;

export type ArchiveDestinationId = (typeof ARCHIVE_HUB_IDS)[number];

export type ArchiveDestination = {
  id: ArchiveDestinationId;
  title: string;
  kicker: string;
  kickerRole: BadgeRole;
  meta: string[];
  icon: ArchiveIconName;
};

/** Static hub rows — titles, kickers, roles, section icons, and copy. Not live counts. */
export const archiveDestinations: ArchiveDestination[] = [
  {
    id: 'stories',
    title: 'Articles',
    kicker: 'Long-form',
    kickerRole: 'restored',
    meta: ['104 features', 'Editorial'],
    icon: 'articles',
  },
  {
    id: 'timeline',
    title: 'Timeline',
    kicker: 'History',
    kickerRole: 'archive',
    meta: ['1970 — 1991', '480 entries'],
    icon: 'timeline',
  },
  {
    id: 'biography',
    title: 'Biography',
    kicker: 'The band',
    kickerRole: 'community',
    meta: ['Nine chapters'],
    icon: 'biography',
  },
  {
    id: 'discography',
    title: 'Discography',
    kicker: 'Records',
    kickerRole: 'community',
    meta: ['15 studio albums', 'Sleeves & tracklists'],
    icon: 'discography',
  },
  {
    id: 'tribute',
    title: 'Freddie Mercury — a tribute',
    kicker: 'In memoriam',
    kickerRole: 'featured',
    meta: ['1946 — 1991', "Members' memories"],
    icon: 'tribute',
  },
  {
    id: 'fan-performances',
    title: 'Fan performances',
    kicker: 'Community',
    kickerRole: 'community',
    meta: ['212 submissions', 'Video & audio'],
    icon: 'fan-performances',
  },
  {
    id: 'recently-restored',
    title: 'Photographs',
    kicker: 'Preserved',
    kickerRole: 'restored',
    meta: ['1,240 photographs', '340 articles'],
    icon: 'restored',
  },
  {
    id: 'trivia',
    title: 'Trivia',
    kicker: 'Queen facts',
    kickerRole: 'archive',
    meta: ['Random facts'],
    icon: 'trivia',
  },
  {
    id: 'about',
    title: 'Queenzone.com, preserved',
    kicker: 'The old site',
    kickerRole: 'community',
    meta: ['How the archive was rebuilt'],
    icon: 'old-site',
  },
];
