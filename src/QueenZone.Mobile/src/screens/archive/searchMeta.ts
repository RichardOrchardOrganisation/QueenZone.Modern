/** Website `/search` example tags — these are live query presets, not fake destinations. */
export const searchQueryPresets = [
  'Bohemian Rhapsody',
  'Live Aid 1985',
  'Freddie Mercury',
  'Wembley 1986',
] as const;

/**
 * Website type filters minus Photo (not indexed). Article and legacy-article
 * share the website label "Articles"; mobile keeps them distinct.
 */
export const searchTypeFilters = [
  { type: null, label: 'All' },
  { type: 'news', label: 'News' },
  { type: 'forum', label: 'Forum' },
  { type: 'article', label: 'Articles' },
  { type: 'legacy-article', label: 'Legacy articles' },
  { type: 'biography', label: 'Biography' },
  { type: 'discography', label: 'Discography' },
  { type: 'song', label: 'Songs' },
  { type: 'timeline', label: 'Timeline' },
  { type: 'fan-performance', label: 'Fan performances' },
] as const;

export type SearchTypeFilter = (typeof searchTypeFilters)[number]['type'];

export function searchTypeLabel(contentType: string): string {
  const match = searchTypeFilters.find((filter) => filter.type === contentType);
  return match?.label ?? contentType;
}

export const searchMinQueryLength = 2;
