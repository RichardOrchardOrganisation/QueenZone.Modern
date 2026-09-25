/** Public `/api/v1/contact` contract (issue #755). Matches website `/contact`. */

import { trimTrailingChar } from '../text/trimRuns.ts';


export const contactApiPath = '/contact';

export type ContactTopic = {
  value: string;
  label: string;
};

export type ContactFieldLimits = {
  minSubjectLength: number;
  maxSubjectLength: number;
  minMessageLength: number;
  maxMessageLength: number;
  maxNameLength: number;
  maxEmailLength: number;
};

export type ContactForm = {
  signedIn: boolean;
  signedInDisplayName: string | null;
  requiresContactDetails: boolean;
  formStamp: string;
  intro: string;
  confirmationTitle: string;
  confirmationMessage: string;
  topics: ContactTopic[];
  limits: ContactFieldLimits;
};

export type ContactSubmitBody = {
  topic: string;
  subject: string;
  message: string;
  name?: string;
  email?: string;
  website?: string;
  formStamp: string;
};

export type ContactSubmitResult = {
  submitted: boolean;
  confirmationTitle: string;
  confirmationMessage: string;
};

export const fallbackContactLimits: ContactFieldLimits = {
  minSubjectLength: 5,
  maxSubjectLength: 200,
  minMessageLength: 20,
  maxMessageLength: 4000,
  maxNameLength: 100,
  maxEmailLength: 256,
};

export function contactApiUrl(apiBaseUrl: string): string {
  const origin = trimTrailingChar(apiBaseUrl, '/');
  return `${origin}/api/v1${contactApiPath}`;
}

export function buildContactSubmitBody(input: {
  topic: string;
  subject: string;
  message: string;
  name: string;
  email: string;
  formStamp: string;
  requiresContactDetails: boolean;
}): ContactSubmitBody {
  const body: ContactSubmitBody = {
    topic: input.topic.trim(),
    subject: input.subject.trim(),
    message: input.message.trim(),
    formStamp: input.formStamp,
  };

  if (input.requiresContactDetails) {
    body.name = input.name.trim();
    body.email = input.email.trim();
  }

  return body;
}

export function parseContactForm(payload: unknown): ContactForm {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Contact form response was empty.');
  }

  const raw = payload as Record<string, unknown>;
  const topics = Array.isArray(raw.topics)
    ? raw.topics.flatMap((item) => {
        if (!item || typeof item !== 'object') {
          return [];
        }

        const topic = item as { value?: unknown; label?: unknown };
        if (typeof topic.value !== 'string' || typeof topic.label !== 'string') {
          return [];
        }

        return [{ value: topic.value, label: topic.label }];
      })
    : [];

  if (topics.length === 0 || typeof raw.formStamp !== 'string' || raw.formStamp.length === 0) {
    throw new Error('Contact form response was missing topics or a form stamp.');
  }

  const limitsRaw = raw.limits && typeof raw.limits === 'object' ? (raw.limits as Record<string, unknown>) : {};

  return {
    signedIn: raw.signedIn === true,
    signedInDisplayName: typeof raw.signedInDisplayName === 'string' ? raw.signedInDisplayName : null,
    requiresContactDetails: raw.requiresContactDetails !== false,
    formStamp: raw.formStamp,
    intro: typeof raw.intro === 'string' ? raw.intro : '',
    confirmationTitle: typeof raw.confirmationTitle === 'string' ? raw.confirmationTitle : 'Thank you',
    confirmationMessage:
      typeof raw.confirmationMessage === 'string'
        ? raw.confirmationMessage
        : 'Thanks — we have your message. The site admin will read it and reply by email if a response is needed.',
    topics,
    limits: {
      minSubjectLength: readPositiveInt(limitsRaw.minSubjectLength, fallbackContactLimits.minSubjectLength),
      maxSubjectLength: readPositiveInt(limitsRaw.maxSubjectLength, fallbackContactLimits.maxSubjectLength),
      minMessageLength: readPositiveInt(limitsRaw.minMessageLength, fallbackContactLimits.minMessageLength),
      maxMessageLength: readPositiveInt(limitsRaw.maxMessageLength, fallbackContactLimits.maxMessageLength),
      maxNameLength: readPositiveInt(limitsRaw.maxNameLength, fallbackContactLimits.maxNameLength),
      maxEmailLength: readPositiveInt(limitsRaw.maxEmailLength, fallbackContactLimits.maxEmailLength),
    },
  };
}

export function parseContactSubmitResult(payload: unknown): ContactSubmitResult {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Contact submit response was empty.');
  }

  const raw = payload as Record<string, unknown>;
  if (raw.submitted !== true) {
    throw new Error('Contact submit did not confirm the message.');
  }

  return {
    submitted: true,
    confirmationTitle: typeof raw.confirmationTitle === 'string' ? raw.confirmationTitle : 'Thank you',
    confirmationMessage:
      typeof raw.confirmationMessage === 'string'
        ? raw.confirmationMessage
        : 'Thanks — we have your message. The site admin will read it and reply by email if a response is needed.',
  };
}

function readPositiveInt(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? Math.trunc(value) : fallback;
}
