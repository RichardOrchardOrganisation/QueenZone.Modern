import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export type ContractMember = {
  id: string;
  email: string;
  displayName: string;
  accessToken: string;
};

export type ContractFixture = {
  baseUrl: string;
  environment: string;
  member: ContractMember;
  otherMember: ContractMember;
  suspendedMember: ContractMember;
  pollTopicId: number;
  pollOptionId: string;
  attachTopicId: number;
  discussionTopicId: number;
  crosswordId: string;
  archivedCrosswordId: string;
};

function fixturePath(): string {
  const configured = process.env.QUEENZONE_MOBILE_CONTRACT_FIXTURE;
  if (configured && configured.trim().length > 0) {
    return path.resolve(configured);
  }

  return path.join(path.dirname(fileURLToPath(import.meta.url)), 'host.json');
}

export function loadContractFixture(): ContractFixture {
  const filePath = fixturePath();
  if (!fs.existsSync(filePath)) {
    throw new Error(
      `Mobile API contract fixture was not found at ${filePath}. Start the Testing host with QUEENZONE_MOBILE_CONTRACT_HOST=1 first (see scripts/run-mobile-api-contracts.sh).`,
    );
  }

  const fixture = JSON.parse(fs.readFileSync(filePath, 'utf8')) as ContractFixture;
  if (!fixture.baseUrl || !fixture.member?.accessToken) {
    throw new Error(`Contract fixture at ${filePath} is missing baseUrl or member.accessToken.`);
  }

  if (fixture.environment !== 'Testing') {
    throw new Error(`Contract host environment was ${fixture.environment}; only Testing is allowed.`);
  }

  process.env.EXPO_PUBLIC_API_BASE_URL = fixture.baseUrl;
  return fixture;
}

export function pngPixel(): Blob {
  const bytes = Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
    'base64',
  );
  return new Blob([bytes], { type: 'image/png' });
}
