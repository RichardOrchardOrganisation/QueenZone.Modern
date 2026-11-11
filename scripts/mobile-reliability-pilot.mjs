// Invoked only by the opt-in device reliability pilot (#2471).
import { readFileSync, writeFileSync, mkdirSync, cpSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

const [command, mode, directory] = process.argv.slice(2);
const [journey, variant, extra] = (mode ?? '').split('-');
const cases = {
  photos: {
    flow: '05-photography.yaml',
    ready: 'tab-photos',
    source: 'navigation/RootNavigator.tsx',
    original: "listeners={reselectRoot('PhotosTab', 'PhotoIndex')}",
    defect: 'listeners={{ tabPress: (event) => event.preventDefault() }}',
    check: 'photos-screen',
  },
  attachment: {
    flow: '10-forum-attach.yaml',
    source: 'screens/forum/ForumAttachmentList.tsx',
    original: '        await openForumAttachmentFile(attachment.downloadUrl, accessToken, attachment.fileName, {',
    defect: "        if (attachment.fileName === 'attach.txt') throw new Error('Pilot download failure');\n"
      + '        await openForumAttachmentFile(attachment.downloadUrl, accessToken, attachment.fileName, {',
    check: 'forum-thread-attachment-opened',
  },
};
const config = Object.hasOwn(cases, journey) ? cases[journey] : undefined;
if (!config || extra !== undefined || !['baseline', 'candidate', 'defect'].includes(variant)) {
  throw new Error('Use photos or attachment with baseline, candidate or defect.');
}

const mobile = 'src/QueenZone.Mobile';
function replaceOnce(file, original, replacement) {
  const source = readFileSync(file, 'utf8');
  if (source.split(original).length !== 2) {
    throw new Error(`Expected one pilot anchor in ${file}`);
  }
  writeFileSync(file, source.replace(original, replacement));
}

function toolFromRoot(variable, executable) {
  const root = process.env[variable];
  if (!root || !isAbsolute(root)) {
    throw new Error(`${variable} must be an absolute tool installation path.`);
  }
  return join(root, executable);
}

if (command === 'prepare') {
  const { FORCE_HOSTED_IOS, GITHUB_SHA, ImageOS, ImageVersion, RUNNER_OS } = process.env;
  if (FORCE_HOSTED_IOS !== 'true') throw new Error('Pilot requires force_hosted_ios=true');
  console.log(`pilot=${mode} commit=${GITHUB_SHA} runner_image=${ImageOS ?? 'unknown'}/${ImageVersion ?? 'unknown'}`);
  console.log(`Node ${process.version}`);
  const java = RUNNER_OS === 'macOS' && !process.env.JAVA_HOME
    ? '/usr/bin/java'
    : toolFromRoot('JAVA_HOME', 'bin/java');
  execFileSync(toolFromRoot('DOTNET_ROOT', 'dotnet'), ['--version'], { stdio: 'inherit' });
  execFileSync(java, ['-version'], { stdio: 'inherit' });
  if (RUNNER_OS === 'macOS') {
    execFileSync('/usr/bin/xcodebuild', ['-version'], { stdio: 'inherit' });
    execFileSync('/usr/bin/xcrun', ['simctl', 'list', 'runtimes'], { stdio: 'inherit' });
  }
  if (variant === 'defect') {
    const source = join(mobile, 'src', config.source);
    replaceOnce(source, config.original, config.defect);
    console.log(`Pilot defect must fail at ${config.check}`);
  }
} else if (command === 'flow' && directory) {
  const workspace = resolve(directory, 'pilot-maestro');
  mkdirSync(workspace, { recursive: true });
  cpSync(join(mobile, 'maestro', 'flows'), join(workspace, 'flows'), { recursive: true });
  const flow = join(workspace, 'flows', config.flow);
  if (variant === 'candidate' || variant === 'defect') {
    const candidate = journey === 'photos'
      ? {
        anchor: /- tapOn:\r?\n {4}id: tab-photos\r?\n/,
        replacement: (tap) => `- extendedWaitUntil:\n    visible:\n      id: ${config.ready}\n    timeout: 10000\n${tap}`,
      }
      : {
        anchor: /id: forum-thread-attachment-opened\r?\n {4}timeout: 20000/,
        replacement: (wait) => wait.replace('20000', '30000'),
      };
    const original = readFileSync(flow, 'utf8').match(candidate.anchor)?.[0];
    if (!original) throw new Error(`Missing candidate anchor for ${journey}`);
    replaceOnce(flow, original, candidate.replacement(original));
  }
  const launch = journey === 'photos' ? '- runFlow: flows/01-launch.yaml\n' : '';
  const entry = join(workspace, 'trial.yaml');
  writeFileSync(entry, `appId: org.queenzone.mobile\nname: Reliability pilot ${mode}\n---\n${launch}- runFlow: flows/${config.flow}\n`);
  console.log(entry);
} else {
  throw new Error('Use prepare MODE or flow MODE RESULTS_DIRECTORY.');
}
