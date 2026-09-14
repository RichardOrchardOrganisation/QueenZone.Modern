import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  messagesApiPath,
  messagesArchivedPath,
  messagesArchivePath,
  messagesConversationPath,
  messagesRecipientsPath,
  messagesReportPath,
  messagesUnarchivePath,
  messagesUnblockPath,
  messagesUnreadCountPath,
} from './messagesPaths.ts';

describe('messages API paths', () => {
  it('nests inbox under the signed-in member API', () => {
    assert.equal(messagesApiPath, '/me/messages');
    assert.equal(messagesUnreadCountPath, '/me/messages/unread-count');
    assert.equal(messagesRecipientsPath, '/me/messages/recipients');
    assert.equal(messagesArchivedPath, '/me/messages/archived');
    assert.equal(
      messagesConversationPath('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),
      '/me/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    );
    assert.equal(
      messagesReportPath(
        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        '11111111-2222-3333-4444-555555555555',
      ),
      '/me/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/messages/11111111-2222-3333-4444-555555555555/report',
    );
    assert.equal(
      messagesArchivePath('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),
      '/me/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/archive',
    );
    assert.equal(
      messagesUnarchivePath('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),
      '/me/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/unarchive',
    );
    assert.equal(
      messagesUnblockPath('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'),
      '/me/messages/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/unblock',
    );
  });
});
