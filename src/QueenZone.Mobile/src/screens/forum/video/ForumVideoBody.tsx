import { useMemo } from 'react';
import { HTMLElementModel, HTMLContentModel, type CustomBlockRenderer } from '@native-html/render';
import { space } from '../../../theme';
import { RichHtmlBody } from '../../../ui/RichHtmlBody';
import { projectVideos } from './descriptors';
import { ForumVideoCard } from './ForumVideoCard';

const models = { 'qz-forum-video': HTMLElementModel.fromCustomModel({ tagName: 'qz-forum-video', contentModel: HTMLContentModel.block }) };
export function ForumVideoBody({ html, descriptors, postKey }: { html: string; descriptors: unknown; postKey: string }) {
  const projection = useMemo(() => projectVideos(html, descriptors), [descriptors, html]);
  const renderers = useMemo(() => {
    const renderer: CustomBlockRenderer = ({ tnode }) => {
      const card = projection.cards[Number(tnode.attributes['data-card'])];
      if (!card) return null;
      return <ForumVideoCard key={`${postKey}:${card.video.anchorIndex}:${card.video.videoId}:${card.video.startSeconds}`}
        cardKey={`${postKey}:${card.video.anchorIndex}:${card.video.videoId}:${card.video.startSeconds}`}
        video={card.video} originalHtml={card.originalHtml} />;
    };
    return { 'qz-forum-video': renderer };
  }, [postKey, projection]);
  if (!projection.cards.length) return <RichHtmlBody html={html} horizontalInset={space.xl} />;
  return <RichHtmlBody html={projection.html} horizontalInset={space.xl} renderers={renderers} customHTMLElementModels={models} />;
}
