import { existsSync } from 'node:fs';
import path from 'node:path';

const knownGitExecutables = [
  '/usr/bin/git',
  '/usr/local/bin/git',
  '/opt/homebrew/bin/git',
  String.raw`C:\Program Files\Git\cmd\git.exe`,
  String.raw`C:\Program Files\Git\mingw64\bin\git.exe`,
];

/** An absolute git path (GIT_EXECUTABLE, else a standard install), so a writable PATH entry cannot supply git. */
export function resolveGitExecutable(env = process.env, exists = existsSync) {
  const configured = env.GIT_EXECUTABLE;
  if (configured) {
    if (!path.isAbsolute(configured)) {
      throw new Error('GIT_EXECUTABLE must be an absolute path.');
    }
    return configured;
  }
  const found = knownGitExecutables.find((candidate) => exists(candidate));
  if (!found) {
    throw new Error(`git not found. Set GIT_EXECUTABLE to an absolute path or install git at ${knownGitExecutables.join(', ')}.`);
  }
  return found;
}
