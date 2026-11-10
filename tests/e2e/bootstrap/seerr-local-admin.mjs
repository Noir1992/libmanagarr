// Runs inside the pinned Seerr image during fresh baseline generation. Seerr's login API
// authenticates existing local users; its own ORM is used to seed the initial test owner.
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';

const require = createRequire('/app/package.json');
const account = JSON.parse(process.argv[1]);
const deadline = Date.now() + 60_000;
let lastFailure;
while (true) {
  try {
    const response = await fetch('http://127.0.0.1:5055/api/v1/settings/public', { signal: AbortSignal.timeout(2000) });
    if (response.ok) break;
    lastFailure = `${response.status}: ${await response.text()}`;
  } catch (error) {
    lastFailure = error.cause?.message ?? error.message;
  }
  assert.ok(Date.now() < deadline, `Seerr did not finish initialization: ${lastFailure}`);
  await delay(250);
}
const { default: dataSource } = require('/app/dist/datasource');
const { User } = require('/app/dist/entity/User');
await dataSource.initialize();
try {
  const users = dataSource.getRepository(User);
  assert.equal(await users.count(), 0, 'Local test owner seeding requires an empty user table');
  const owner = users.create({
    id: 1,
    email: account.email,
    username: account.username,
    userType: 2,
    permissions: 2,
    avatar: '',
  });
  await owner.setPassword(account.password);
  await users.save(owner);
  console.log('Local Seerr test owner created.');
} finally {
  await dataSource.destroy();
}
