import { spawn, spawnSync } from 'node:child_process';

const env = {
  ...process.env,
  VITE_API_BASE: 'http://127.0.0.1:5000',
};
const useShell = process.platform === 'win32';
const npmCommand = 'npm';

function runNpmSync(args, options) {
  if (useShell) {
    return spawnSync(`npm ${args.join(' ')}`, { ...options, shell: true });
  }

  return spawnSync(npmCommand, args, options);
}

function spawnNpm(args, options) {
  if (useShell) {
    return spawn(`npm ${args.join(' ')}`, { ...options, shell: true });
  }

  return spawn(npmCommand, args, options);
}

const build = runNpmSync(['run', 'build'], {
  stdio: 'inherit',
  env,
});

if (build.error) {
  console.error(build.error.message);
}

if (build.status !== 0) {
  process.exit(build.status ?? 1);
}

const preview = spawnNpm(['run', 'preview', '--', '--host', '127.0.0.1', '--port', '4173'], {
  stdio: 'inherit',
  env,
});

const stopPreview = () => {
  if (!preview.killed) {
    preview.kill();
  }
};

process.on('SIGINT', stopPreview);
process.on('SIGTERM', stopPreview);
preview.on('exit', (code) => process.exit(code ?? 0));
