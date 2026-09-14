import { memo, useCallback, useReducer } from 'react';
import { Alert, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { Flag } from 'lucide-react-native';
import { removeOfflineItem, updateOfflineItem, flushOfflineQueue } from '../../offlineQueue';
import { fonts, palette, radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { testIds } from '../../test/testIds';
import {
  formatMessageClockTime,
  initialsFor,
  reportReasonMaxLength,
  validateReportReason,
} from './inboxMeta';
import { messageFromUnknownError, queueStatusLabel, type DisplayMessage } from './conversationMeta';

/** Test-only. Production leaves this unset so bubble commits stay uninstrumented. */
export const messageBubbleRenderProbe: { current: (() => void) | null } = {
  current: null,
};

type ReportState = {
  reporting: boolean;
  reportReason: string;
  reportError: string | null;
  reportBusy: boolean;
};

const initialReportState: ReportState = {
  reporting: false,
  reportReason: '',
  reportError: null,
  reportBusy: false,
};

type ReportAction =
  | { type: 'start' }
  | { type: 'cancel' }
  | { type: 'changeReason'; reason: string }
  | { type: 'submitStart' }
  | { type: 'submitSuccess' }
  | { type: 'submitFailure'; message: string };

function reportReducer(state: ReportState, action: ReportAction): ReportState {
  switch (action.type) {
    case 'start':
      return { ...initialReportState, reporting: true };
    case 'cancel':
      return initialReportState;
    case 'changeReason':
      return { ...state, reportReason: action.reason };
    case 'submitStart':
      return { ...state, reportBusy: true, reportError: null };
    case 'submitSuccess':
      return initialReportState;
    case 'submitFailure':
      return { ...state, reportBusy: false, reportError: action.message };
    default:
      return state;
  }
}

export const MessageBubble = memo(function MessageBubble({
  item,
  correspondentName,
  isFirstOfRun,
  interactionsEnabled,
  onSubmitReport,
}: {
  item: DisplayMessage;
  correspondentName: string;
  isFirstOfRun: boolean;
  interactionsEnabled: boolean;
  onSubmitReport: (messageId: string, reason?: string) => Promise<void>;
}) {
  messageBubbleRenderProbe.current?.();
  const { c } = useTheme();
  const [report, dispatch] = useReducer(reportReducer, initialReportState);

  const startReport = useCallback(() => {
    dispatch({ type: 'start' });
  }, []);

  const cancelReport = useCallback(() => {
    dispatch({ type: 'cancel' });
  }, []);

  const submitReport = useCallback(async () => {
    const validation = validateReportReason(report.reportReason);
    if (validation) {
      dispatch({ type: 'submitFailure', message: validation });
      return;
    }
    dispatch({ type: 'submitStart' });
    try {
      await onSubmitReport(item.id, report.reportReason.trim() || undefined);
      dispatch({ type: 'submitSuccess' });
    } catch (err: unknown) {
      dispatch({ type: 'submitFailure', message: messageFromUnknownError(err) });
    }
  }, [item.id, onSubmitReport, report.reportReason]);

  const time = formatMessageClockTime(item.createdAt);
  const attribution = item.isMine
    ? `YOU · ${time}`
    : isFirstOfRun
      ? `${item.senderDisplayName.toUpperCase()} · ${time}`
      : time;
  const attributionColor = item.isMine ? c.textMuted : c.textSecondary;

  if (item.isMine) {
    return (
      <View style={styles.outgoingRow}>
        <Text style={[styles.attribution, { color: attributionColor }]}>{attribution}</Text>
        <View
          style={[
            styles.bubble,
            { backgroundColor: c.bubbleOutgoing, borderWidth: 1, borderColor: c.borderStrong },
          ]}
        >
          <Text style={[styles.outgoingText, { color: c.textPrimary }]}>{item.body}</Text>
        </View>
        {item.queueState ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={queueStatusLabel(item.queueState)}
            testID={testIds.pendingMessage}
            onPress={() => {
              if (item.queueState !== 'needs_attention') {
                return;
              }
              Alert.alert(item.queueError ?? 'This message could not be sent.', undefined, [
                { text: 'Dismiss', style: 'cancel' },
                {
                  text: 'Discard',
                  style: 'destructive',
                  onPress: () => {
                    void removeOfflineItem(item.id);
                  },
                },
                {
                  text: 'Retry',
                  onPress: () => {
                    void updateOfflineItem(item.id, {
                      state: 'queued',
                      nextRetryAt: new Date().toISOString(),
                      lastError: null,
                    }).then(() => {
                      void flushOfflineQueue();
                    });
                  },
                },
              ]);
            }}
          >
            <Text style={[styles.attribution, { color: c.accentPrimary }]}>
              {queueStatusLabel(item.queueState)}
            </Text>
          </Pressable>
        ) : null}
      </View>
    );
  }

  return (
    <View style={styles.incomingRow}>
      {isFirstOfRun ? (
        <View style={[styles.avatar, styles.incomingAvatar, { backgroundColor: palette.burgundy }]}>
          <Text style={styles.avatarLabelSmall}>{initialsFor(correspondentName)}</Text>
        </View>
      ) : (
        <View style={styles.avatarSpacer} />
      )}
      <View style={styles.incomingContent}>
        <Text style={[styles.attribution, { color: attributionColor }]}>{attribution}</Text>
        <View style={[styles.bubble, styles.incomingBubble]}>
          <Text style={styles.incomingText}>{item.body}</Text>
        </View>
        {item.reportedByViewer ? (
          <View style={styles.reportedRow}>
            <Flag size={11} strokeWidth={2} color={c.accentPrimary} />
            <Text style={[styles.reportedLabel, { color: c.accentPrimary }]}>REPORTED</Text>
          </View>
        ) : !interactionsEnabled ? null : report.reporting ? (
          <View style={styles.reportForm}>
            <TextInput
              value={report.reportReason}
              onChangeText={(reason) => dispatch({ type: 'changeReason', reason })}
              placeholder="Optional reason"
              placeholderTextColor={c.textMuted}
              accessibilityLabel="Optional reason"
              maxLength={reportReasonMaxLength}
              editable={!report.reportBusy}
              style={[
                styles.reportField,
                { borderColor: c.borderStrong, backgroundColor: c.surfaceCard, color: c.textPrimary },
              ]}
            />
            {report.reportError ? (
              <Text style={[type.caption, { color: c.danger }]}>{report.reportError}</Text>
            ) : null}
            <View style={styles.reportActions}>
              <Button
                label="Submit report"
                size="sm"
                onPress={() => {
                  void submitReport();
                }}
                loading={report.reportBusy}
              />
              <Button label="Cancel" size="sm" variant="ghost" onPress={cancelReport} disabled={report.reportBusy} />
            </View>
          </View>
        ) : (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Report message"
            onPress={startReport}
            hitSlop={8}
          >
            <Text style={[styles.reportTrigger, { color: c.textMuted }]}>Report message</Text>
          </Pressable>
        )}
      </View>
    </View>
  );
});

const styles = StyleSheet.create({
  avatar: { borderRadius: 999, alignItems: 'center', justifyContent: 'center' },
  avatarLabelSmall: {
    fontFamily: fonts.titling,
    fontSize: 10,
    letterSpacing: 0.6,
    color: palette.white,
  },
  avatarSpacer: { width: 28, height: 28 },
  incomingAvatar: { width: 28, height: 28, marginTop: 18 },
  attribution: {
    fontFamily: fonts.titling,
    fontSize: 9.5,
    letterSpacing: 1.9,
  },
  outgoingRow: { alignItems: 'flex-end', gap: 6 },
  incomingRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 10 },
  incomingContent: { flex: 1, maxWidth: '80%', alignItems: 'flex-start', gap: 6 },
  bubble: { borderRadius: radius.md, paddingHorizontal: 14, paddingVertical: 12, maxWidth: '80%' },
  incomingBubble: { backgroundColor: palette.warmWhite, alignSelf: 'stretch', maxWidth: undefined },
  outgoingText: { fontFamily: fonts.body, fontSize: 16.5, lineHeight: 24.75 },
  incomingText: { fontFamily: fonts.body, fontSize: 16.5, lineHeight: 24.75, color: palette.charcoal },
  reportedRow: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingLeft: 2 },
  reportedLabel: { fontFamily: fonts.titling, fontSize: 9, letterSpacing: 1.6 },
  reportTrigger: {
    fontFamily: fonts.body,
    fontSize: 13,
    textDecorationLine: 'underline',
  },
  reportForm: { alignSelf: 'stretch', gap: space.sm },
  reportField: {
    minHeight: 40,
    borderWidth: 1,
    borderRadius: radius.md,
    paddingHorizontal: 12,
    paddingVertical: 8,
    fontFamily: fonts.body,
    fontSize: 16,
  },
  reportActions: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
});
