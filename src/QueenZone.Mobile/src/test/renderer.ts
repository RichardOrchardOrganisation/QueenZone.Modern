import { screen } from '@testing-library/react-native';
import type { ComponentProps, ElementType } from 'react';

type Fiber = NonNullable<NonNullable<typeof screen.root>['unstable_fiber']>;

/** Inspect composite configuration that is intentionally absent from host queries.
 * These assertions cover native layout/list settings and guarded callbacks.
 */
export function allComponentProps<T extends ElementType>(component: T): { props: ComponentProps<T> }[] {
  let root = screen.root?.unstable_fiber ?? null;
  while (root?.return) root = root.return;
  const matches: { props: ComponentProps<T> }[] = [];
  function visit(fiber: Fiber | null) {
    if (!fiber) return;
    if (fiber.type === component) matches.push({ props: fiber.memoizedProps });
    visit(fiber.child);
    visit(fiber.sibling);
  }
  // Host instances may retain an alternate Fiber after an update. Read the
  // committed root so callback and configuration assertions use current props.
  visit(root?.stateNode.current ?? root);
  expect(matches.length).toBeGreaterThan(0);
  return matches;
}

export function componentProps<T extends ElementType>(component: T): { props: ComponentProps<T> } {
  const matches = allComponentProps(component);
  expect(matches).toHaveLength(1);
  return matches[0];
}

/** A strict host query for native controls without an accessibility query. */
export function getHostByType(type: string) {
  const matches = screen.container.queryAll((node) => node.type === type);
  expect(matches).toHaveLength(1);
  return matches[0];
}
