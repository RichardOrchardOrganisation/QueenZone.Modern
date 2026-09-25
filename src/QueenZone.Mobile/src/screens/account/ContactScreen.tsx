import { useCallback, useEffect, useState } from 'react';
import {
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getAppConfig } from '../../config/appConfig';
import {
  buildContactSubmitBody,
  contactApiUrl,
  fallbackContactLimits,
  parseContactForm,
  parseContactSubmitResult,
  type ContactForm,
} from '../../api/contact';
import { readProblemDetail } from '../../api/problemDetail';
import { radius, space, type, useTheme } from '../../theme';
import { FormFieldLabel, FormScreenLayout } from '../../ui/FormScreenLayout';

const defaultTopic = 'Other';

export function ContactScreen() {
  const insets = useSafeAreaInsets();
  const { c } = useTheme();
  const [form, setForm] = useState<ContactForm | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [confirmation, setConfirmation] = useState<{ title: string; message: string } | null>(null);
  const [topic, setTopic] = useState(defaultTopic);
  const [subject, setSubject] = useState('');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [message, setMessage] = useState('');

  const loadForm = useCallback(async () => {
    setLoadError(null);
    try {
      const response = await fetch(contactApiUrl(getAppConfig().apiBaseUrl), {
        headers: { Accept: 'application/json' },
      });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(readProblemDetail(payload, 'Could not load the contact form.'));
      }

      const next = parseContactForm(payload);
      setForm(next);
      setTopic((current) =>
        next.topics.some((item) => item.value === current) ? current : (next.topics[0]?.value ?? defaultTopic),
      );
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : 'Could not load the contact form.');
    }
  }, []);

  useEffect(() => {
    void loadForm();
  }, [loadForm]);

  const limits = form?.limits ?? fallbackContactLimits;
  const requiresContactDetails = form?.requiresContactDetails ?? true;

  async function onSubmit() {
    if (!form || submitting) {
      return;
    }

    setSubmitError(null);
    setSubmitting(true);
    try {
      const response = await fetch(contactApiUrl(getAppConfig().apiBaseUrl), {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(
          buildContactSubmitBody({
            topic,
            subject,
            message,
            name,
            email,
            formStamp: form.formStamp,
            requiresContactDetails,
          }),
        ),
      });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(readProblemDetail(payload, 'Could not send your message.'));
      }

      const result = parseContactSubmitResult(payload);
      setConfirmation({ title: result.confirmationTitle, message: result.confirmationMessage });
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : 'Could not send your message.');
    } finally {
      setSubmitting(false);
    }
  }

  async function onSendAnother() {
    setConfirmation(null);
    setSubject('');
    setMessage('');
    setSubmitError(null);
    await loadForm();
  }

  return (
    <FormScreenLayout backgroundColor={c.surfacePage} bottomInset={insets.bottom}>
        <Text style={[type.eyebrow, { color: c.accentPrimary }]}>Contact</Text>
        <Text style={[type.pageTitle, { color: c.textPrimary }]} maxFontSizeMultiplier={1.4} allowFontScaling>
          Contact us
        </Text>
        <Text style={[type.body, { color: c.textSecondary }]} allowFontScaling>
          {form?.intro ??
            'Having trouble with your account or the archive? Send a private message to the Queenzone admin.'}
        </Text>

        {form?.signedIn ? (
          <Text style={[type.caption, { color: c.textMuted }]} allowFontScaling>
            Signed in as {form.signedInDisplayName ?? 'member'}. We will use the name and email on your account.
          </Text>
        ) : null}

        {loadError ? (
          <View style={[styles.notice, { borderColor: c.danger }]}>
            <Text style={[type.body, { color: c.danger }]}>{loadError}</Text>
            <Pressable accessibilityRole="button" accessibilityLabel="Retry loading the contact form" onPress={() => void loadForm()}>
              <Text style={[type.button, { color: c.accentPrimary, marginTop: space.sm }]}>Retry</Text>
            </Pressable>
          </View>
        ) : null}

        {confirmation ? (
          <View style={[styles.notice, { borderColor: c.accentPrimary }]} accessibilityRole="text">
            <Text style={[type.cardTitle, { color: c.textPrimary }]}>{confirmation.title}</Text>
            <Text style={[type.body, { color: c.textSecondary, marginTop: space.sm }]} allowFontScaling>
              {confirmation.message}
            </Text>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Send another message"
              onPress={() => void onSendAnother()}
              style={({ pressed }) => [styles.button, { borderColor: c.border, marginTop: space.base }, pressed && styles.pressed]}
            >
              <Text style={[type.button, { color: c.accentPrimary }]}>Send another message</Text>
            </Pressable>
          </View>
        ) : (
          <View style={styles.fields}>
            <FormFieldLabel color={c.textMuted}>Topic</FormFieldLabel>
            <View style={styles.topics}>
              {(form?.topics ?? [{ value: defaultTopic, label: 'Other' }]).map((item) => {
                const selected = item.value === topic;
                return (
                  <Pressable
                    key={item.value}
                    accessibilityRole="button"
                    accessibilityState={{ selected }}
                    accessibilityLabel={item.label}
                    onPress={() => setTopic(item.value)}
                    style={({ pressed }) => [
                      styles.topic,
                      {
                        backgroundColor: selected ? c.accentTintWeak : c.surfaceCard,
                        borderColor: selected ? c.accentPrimary : c.border,
                      },
                      pressed && styles.pressed,
                    ]}
                  >
                    <Text style={[type.caption, { color: selected ? c.accentPrimary : c.textSecondary }]}>
                      {item.label}
                    </Text>
                  </Pressable>
                );
              })}
            </View>

            <FormFieldLabel color={c.textMuted}>Subject</FormFieldLabel>
            <TextInput
              value={subject}
              onChangeText={setSubject}
              maxLength={limits.maxSubjectLength}
              accessibilityLabel="Subject"
              placeholder="Short summary"
              placeholderTextColor={c.textMuted}
              style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
            />

            {requiresContactDetails ? (
              <>
                <FormFieldLabel color={c.textMuted}>Your name</FormFieldLabel>
                <TextInput
                  value={name}
                  onChangeText={setName}
                  maxLength={limits.maxNameLength}
                  autoComplete="name"
                  accessibilityLabel="Your name"
                  placeholder="Name"
                  placeholderTextColor={c.textMuted}
                  style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
                />

                <FormFieldLabel color={c.textMuted}>Email address</FormFieldLabel>
                <TextInput
                  value={email}
                  onChangeText={setEmail}
                  maxLength={limits.maxEmailLength}
                  autoComplete="email"
                  keyboardType="email-address"
                  autoCapitalize="none"
                  accessibilityLabel="Email address"
                  placeholder="you@example.com"
                  placeholderTextColor={c.textMuted}
                  style={[styles.input, { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard }]}
                />
                <Text style={[type.caption, { color: c.textMuted }]}>We only use this to reply to your request.</Text>
              </>
            ) : null}

            <FormFieldLabel color={c.textMuted}>Your message</FormFieldLabel>
            <TextInput
              value={message}
              onChangeText={setMessage}
              maxLength={limits.maxMessageLength}
              multiline
              textAlignVertical="top"
              accessibilityLabel="Your message"
              placeholder="Plain text is fine — include account names, page addresses, or error messages if they help."
              placeholderTextColor={c.textMuted}
              style={[
                styles.input,
                styles.textarea,
                { color: c.textPrimary, borderColor: c.border, backgroundColor: c.surfaceCard },
              ]}
            />

            {submitError ? (
              <Text style={[type.body, { color: c.danger }]} accessibilityRole="alert">
                {submitError}
              </Text>
            ) : null}

            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Send message"
              accessibilityState={{ disabled: submitting || !form }}
              disabled={submitting || !form}
              onPress={() => void onSubmit()}
              style={({ pressed }) => [
                styles.submit,
                { backgroundColor: c.accentPrimary, opacity: submitting || !form ? 0.6 : 1 },
                pressed && styles.pressed,
              ]}
            >
              <Text style={[type.button, { color: c.textOnAccent }]}>
                {submitting ? 'Sending' : 'Send message'}
              </Text>
            </Pressable>
          </View>
        )}
    </FormScreenLayout>
  );
}

const styles = StyleSheet.create({
  fields: {
    gap: space.sm,
  },
  topics: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: space.sm,
  },
  topic: {
    borderWidth: 1,
    borderRadius: radius.pill,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
    minHeight: 40,
    justifyContent: 'center',
  },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
    fontFamily: type.body.fontFamily,
    fontSize: type.body.fontSize,
  },
  textarea: {
    minHeight: 160,
  },
  notice: {
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.base,
    gap: space.sm,
  },
  button: {
    minHeight: 48,
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 1,
    borderRadius: radius.xs,
    paddingHorizontal: space.base,
  },
  submit: {
    minHeight: 48,
    justifyContent: 'center',
    alignItems: 'center',
    borderRadius: radius.xs,
    marginTop: space.sm,
  },
  pressed: {
    opacity: 0.85,
  },
});
