import { spawn } from 'node:child_process';
import { createWriteStream } from 'node:fs';

export function execute(
  command,
  args,
  { cwd, env = process.env, capture = false, timeout = 1_200_000, logFile, signal: abortSignal } = {},
) {
  return new Promise((resolve, reject) => {
    const piped = capture || !!logFile;
    const log = logFile ? createWriteStream(logFile, { flags: 'a' }) : undefined;
    log?.once('error', reject);
    const child = spawn(command, args, {
      cwd,
      env,
      stdio: piped ? ['ignore', 'pipe', 'pipe'] : 'inherit',
      windowsHide: true,
      signal: abortSignal,
    });
    let output = '';
    if (piped) {
      child.stdout.on('data', (data) => {
        output = (output + data).slice(-1_000_000);
        log?.write(data);
        if (!capture) process.stdout.write(data);
      });
      child.stderr.on('data', (data) => {
        output = (output + data).slice(-1_000_000);
        log?.write(data);
        if (!capture) process.stderr.write(data);
      });
    }
    const timer = setTimeout(() => child.kill(), timeout);
    child.once('error', (error) => {
      clearTimeout(timer);
      log?.end();
      reject(error);
    });
    child.once('close', (code, signal) => {
      clearTimeout(timer);
      log?.end();
      if (code !== 0)
        reject(
          Object.assign(new Error(`${command} ${args.join(' ')} failed (${code ?? signal})\n${output}`), { exitCode: code ?? 1, output }),
        );
      else resolve(output);
    });
  });
}
