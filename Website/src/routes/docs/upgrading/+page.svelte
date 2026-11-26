<script lang="ts">
	import CodeBlock from '$lib/CodeBlock.svelte';
</script>

<h1>Upgrading</h1>

<h2>From the release image</h2>
<p>Run these in the folder that holds <code>compose.yaml</code> and <code>.env</code>.</p>
<ol>
	<li>
		Back up the database while ChaosLab is running, and keep <code>.env</code> with the backup. Stored
		target credentials can only be read with the same credentials key, so never change it during an
		upgrade.
		<CodeBlock code="docker compose exec -T postgres pg_dump -U postgres chaos_db > chaoslab-backup.sql" />
	</li>
	<li>
		Pull the new image and restart. The backend applies pending database migrations when it starts.
		<CodeBlock code="docker compose pull && docker compose up -d" />
	</li>
</ol>

<h3>Rolling back</h3>
<p>
	<code>compose.yaml</code> runs the <code>latest</code> image. To go back, set its
	<code>image:</code> to the version you upgraded from, such as
	<code>ghcr.io/neil-urk12/chaoslab:0.1.0</code>, then restore the backup into a fresh database.
	Anything stored since the backup is lost.
</p>
<!-- One chain, so a failed step stops before the database is dropped. -->
<CodeBlock
	code={`docker compose stop chaoslab &&
docker compose exec -T postgres dropdb -U postgres --force chaos_db &&
docker compose exec -T postgres createdb -U postgres chaos_db &&
docker compose exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 chaos_db < chaoslab-backup.sql &&
docker compose up -d`}
/>

<h2>From source</h2>

<h3>Back up first</h3>
<p>With the pod running, tag the commit you are on and dump the database:</p>
<CodeBlock
	code={'git tag -f chaoslab-pre-upgrade\npodman exec chaoslab-stack-postgres pg_dump -U postgres chaos_db > chaoslab-backup.sql'}
/>
<p>
	Keep <code>secret.yaml</code> and <code>Backend/.env.local</code> with the backup. Stored target
	credentials can only be read with the same <code>Credentials__Key</code>, so never change it during an
	upgrade.
</p>

<h3>Upgrade</h3>
<ol>
	<li>
		Stop <code>make start</code>, then pull the new code, rebuild the backend image, apply new
		migrations and restart the pod:
		<CodeBlock code={'git pull\nmake build && make migrate && make restart'} />
	</li>
	<li>
		Update the frontend's packages and start again:
		<CodeBlock code="(cd Frontend && pnpm install) && make start" />
	</li>
</ol>

<h3>Rolling back</h3>
<p>
	If the new version does not work, stop <code>make start</code>, go back to the tagged commit and
	restore the backup into a fresh database. Anything stored since the backup is lost.
</p>
<!-- One chain, so a failed checkout stops before the database is dropped. -->
<CodeBlock
	code={`git checkout chaoslab-pre-upgrade &&
podman stop chaoslab-stack-backend &&
podman exec chaoslab-stack-postgres dropdb -U postgres --force chaos_db &&
podman exec chaoslab-stack-postgres createdb -U postgres chaos_db &&
podman exec -i chaoslab-stack-postgres psql -U postgres -v ON_ERROR_STOP=1 chaos_db < chaoslab-backup.sql &&
make build && make restart`}
/>
<p>
	Then start again with <code>(cd Frontend && pnpm install) && make start</code>. Your clone is now on
	a detached commit: run <code>git switch develop</code> before the next upgrade.
</p>
