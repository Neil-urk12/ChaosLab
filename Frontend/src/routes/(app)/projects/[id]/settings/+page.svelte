<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import Toggle from '$lib/components/Toggle.svelte';
	import type { EnvironmentKind, GlobalHeader } from '$lib/api';
	import { dateTime } from '$lib/format';
	import { problemMessage, send } from '$lib/problem';
	import SchemeCredential from './SchemeCredential.svelte';

	let { data } = $props();
	const project = $derived(data.project);
	const settings = $derived(data.settings);
	const target = $derived(settings.target);
	const api = $derived(`/api/projects/${project.id}/environment`);
	// Viewers get a read-only page; the backend answers 403 regardless.
	const myRole = $derived(project.members.find((m) => m.userId === data.me.id)?.role);
	const canEdit = $derived(myRole === 'Owner' || myRole === 'Editor');

	const icon = {
		menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
		target: '<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="4"/>',
		lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 018 0v3"/>',
		globe: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 010 18M12 3a14 14 0 000 18"/>',
		trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>',
		refresh: '<path d="M20 11a8 8 0 00-14.7-4.3L4 8M4 4v4h4M4 13a8 8 0 0014.7 4.3L20 16M20 20v-4h-4"/>',
		info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>'
	};
	const sections = [
		{ id: 'general', label: 'General', icon: icon.menu },
		{ id: 'target', label: 'Target API', icon: icon.target },
		{ id: 'credentials', label: 'Credentials', icon: icon.lock },
		{ id: 'headers', label: 'Global headers', icon: icon.globe },
		{ id: 'danger', label: 'Danger zone', icon: icon.trash }
	];
	// General and Danger zone are not built yet.
	const built = ['target', 'credentials', 'headers'];
	let section = $state('target');

	// ---- Target ----
	const kinds: [EnvironmentKind, string][] = [
		['Development', 'Development'],
		['Test', 'Test'],
		['Staging', 'Staging'],
		['ProductionMirror', 'Production mirror']
	];
	let baseUrl = $derived(target?.baseUrl ?? settings.specServers.find((s) => /^https?:/.test(s)) ?? '');
	let kind = $derived<EnvironmentKind>(target?.kind ?? 'Test');
	let timeout = $derived(target?.requestTimeoutSeconds ?? 30);
	let verifyTls = $derived(target?.verifyTls ?? true);
	// A new or changed base URL needs a fresh confirmation, so editing the URL clears the box.
	let authorized = $derived(!!target?.authorizedAt && baseUrl.trim().replace(/\/+$/, '') === target.baseUrl);
	let targetBusy = $state(false);
	let targetError = $state('');
	let targetSaved = $state(false);
	let check = $state<{ reachable: boolean; status: number | null; error: string | null; elapsedMs: number } | null>(null);
	let checking = $state(false);

	async function saveTarget() {
		targetBusy = true;
		targetSaved = false;
		targetError =
			(await send(api, 'PUT', { baseUrl, kind, requestTimeoutSeconds: Number(timeout), verifyTls, authorized })) ?? '';
		if (!targetError) {
			await invalidateAll();
			targetSaved = true;
		}
		targetBusy = false;
	}

	async function testConnection() {
		checking = true;
		targetError = '';
		try {
			const res = await fetch(`${api}/check`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ baseUrl, verifyTls, requestTimeoutSeconds: Number(timeout) })
			});
			if (res.ok) check = await res.json();
			else targetError = await problemMessage(res);
		} catch {
			targetError = 'Could not reach the server.';
		} finally {
			checking = false;
		}
	}

	// ---- Credentials ----
	const configured = $derived(settings.schemes.filter((s) => s.credential && s.credential.authType !== 'None'));
	const credentialBadge = $derived(
		!configured.length
			? null
			: configured.some((s) => s.credential?.valid === false)
				? ['Validation failed', 'bg-rose-500/10 text-rose-300']
				: configured.every((s) => s.credential?.valid)
					? ['Validated', 'bg-emerald-500/10 text-emerald-300']
					: null
	);
	const schemeSummary = $derived(
		settings.schemes
			.filter((s) => s.inSpec)
			.map((s) => {
				const how = s.type === 'apiKey' ? `${s.in ?? 'header'} ${s.parameterName ?? ''}`.trim() : s.httpScheme ? `HTTP ${s.httpScheme}` : s.type;
				return `${s.name} (${how}, ${s.operations.length} operation${s.operations.length === 1 ? '' : 's'})`;
			})
			.join(' and ')
	);

	// ---- Headers ----
	let adding = $state(false);
	let header = $state({ name: '', value: '', secret: false, appliesTo: '' });
	let headerError = $state('');

	async function addHeader() {
		headerError =
			(await send(`${api}/headers`, 'POST', {
				...header,
				appliesTo: header.appliesTo.split(',').map((s) => s.trim()).filter(Boolean)
			})) ?? '';
		if (!headerError) {
			adding = false;
			header = { name: '', value: '', secret: false, appliesTo: '' };
			await invalidateAll();
		}
	}

	async function updateHeader(row: GlobalHeader, method: 'PATCH' | 'DELETE') {
		headerError = (await send(`${api}/headers/${row.id}`, method, method === 'PATCH' ? { secret: true } : undefined)) ?? '';
		if (!headerError) await invalidateAll();
	}
</script>

<svelte:head><title>Settings · {project.name} · Chaos Lab</title></svelte:head>

{#snippet svg(path: string, cls = 'size-4')}
	<svg viewBox="0 0 24 24" class={cls} fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{@html path}</svg>
{/snippet}

{#snippet cardTitle(title: string, subtitle: string, path: string)}
	<h3 class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(path, 'size-4 text-accent-ink')}{title}</h3>
	<p class="mt-1 text-xs text-slate-500">{subtitle}</p>
{/snippet}

<div class="mt-8">
	<h2 class="text-xl font-bold">Settings</h2>
	<p class="mt-1 text-[13px] text-slate-400">Where ChaosLab sends test requests and how it authenticates. Production targets are not supported.</p>
</div>

<div class="mt-6 grid items-start gap-6 lg:grid-cols-[15rem_minmax(0,1fr)]">
	<nav aria-label="Settings sections" class="rounded-lg border border-line p-2 lg:sticky lg:top-6">
		{#each sections as item (item.id)}
			{@const danger = item.id === 'danger'}
			{#if built.includes(item.id)}
				<a href="#{item.id}" onclick={() => (section = item.id)} aria-current={section === item.id ? 'true' : undefined}
					class="flex items-center gap-3 rounded-md px-3 py-2 text-[13px] {section === item.id ? 'bg-accent-tint text-accent-ink-strong' : 'text-slate-300 hover:bg-line-faint'}">
					{@render svg(item.icon)}{item.label}
				</a>
			{:else}
				<span title="Not available yet" class="flex items-center gap-3 rounded-md px-3 py-2 text-[13px] {danger ? 'text-rose-400/60' : 'text-slate-500'}">{@render svg(item.icon)}{item.label}</span>
			{/if}
		{/each}
	</nav>

	<div class="min-w-0 space-y-6">
		<section id="target" class="scroll-mt-6 rounded-lg border border-line">
			<div class="flex flex-wrap items-start justify-between gap-4 border-b border-line-faint p-6">
				<div>{@render cardTitle('Target API', 'Validated before every run. If the target is unreachable, nothing runs.', icon.target)}</div>
				{#if check}
					<span role="status" class="flex items-center gap-2 rounded px-2 py-1 text-[11px] {check.reachable ? 'bg-emerald-500/10 text-emerald-300' : 'bg-rose-500/10 text-rose-300'}">
						<span class="size-1.5 rounded-full bg-current"></span>
						{check.reachable ? `Reachable · ${check.status} in ${check.elapsedMs} ms` : 'Unreachable'}
					</span>
				{/if}
			</div>
			<form class="space-y-6 p-6" onsubmit={(e) => (e.preventDefault(), saveTarget())}>
				<label class="block text-xs text-slate-400">Base URL
					<span class="relative mt-2 block">
						<span class="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-slate-500">{@render svg(icon.globe)}</span>
						<input type="url" bind:value={baseUrl} required disabled={!canEdit} placeholder="https://api.example.test/v1" class="auth-input pl-9" />
					</span>
					<span class="mt-2 block text-[11px] text-slate-500">
						Must be an absolute HTTP(S) URL.{#if settings.specServers.length} Servers from the spec: {settings.specServers.join(', ')}{/if}
					</span>
				</label>

				<div class="grid gap-6 md:grid-cols-2">
					<label class="block text-xs text-slate-400">Environment label
						<select bind:value={kind} disabled={!canEdit} class="auth-input mt-2">
							{#each kinds as [value, text] (value)}<option {value}>{text}</option>{/each}
						</select>
						<span class="mt-2 block text-[11px] text-slate-500">Development, Test, Staging, or Production mirror.</span>
					</label>
					<label class="block text-xs text-slate-400">Request timeout budget (s)
						<input type="number" min="1" max="300" bind:value={timeout} required disabled={!canEdit} class="auth-input mt-2" />
						<span class="mt-2 block text-[11px] text-slate-500">Upper bound per request so a hung target can't stall a run.</span>
					</label>
				</div>

				<div class="flex items-center justify-between gap-6 border-y border-line-faint py-5">
					<div>
						<p class="text-[13px]">Verify TLS certificates</p>
						<p class="mt-1 text-[11px] text-slate-500">Turn off only for self-signed certificates on local or test hosts.</p>
					</div>
					<Toggle checked={verifyTls} disabled={!canEdit} label="Verify TLS certificates" onchange={(on) => (verifyTls = on)} />
				</div>

				<label class="flex gap-3 rounded-md border border-line p-4">
					<input type="checkbox" bind:checked={authorized} disabled={!canEdit} class="mt-0.5 rounded border-line-strong bg-field text-accent focus:ring-accent" />
					<span>
						<span class="block text-[13px]">I own this API or have explicit authorization to test it</span>
						<span class="mt-1 block text-[11px] text-slate-500">
							and it is not a production environment. Required before saving a target.{#if target?.authorizedBy && target.authorizedAt && authorized}
								Confirmed by {target.authorizedBy} on {dateTime(target.authorizedAt)}.{/if}
						</span>
					</span>
				</label>

				{#if targetError}<p class="text-[13px] text-rose-300" role="alert">{targetError}</p>{/if}
				{#if check?.error}<p class="text-[13px] text-rose-300" role="alert">{check.error}</p>{/if}
				{#if targetSaved}<p class="text-[13px] text-emerald-300" role="status">Target saved.</p>{/if}

				{#if canEdit}
					<div class="flex justify-end gap-3">
						<button type="button" onclick={testConnection} disabled={checking} class="flex h-9 items-center gap-2 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint disabled:opacity-60">
							{@render svg(icon.refresh)}{checking ? 'Testing…' : 'Test connection'}
						</button>
						<button type="submit" disabled={targetBusy || !authorized} class="h-9 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60">Save target</button>
					</div>
				{/if}
			</form>
		</section>

		<section id="credentials" class="scroll-mt-6 rounded-lg border border-line">
			<div class="flex flex-wrap items-start justify-between gap-4 border-b border-line-faint p-6">
				<div>{@render cardTitle('Credentials', 'Test credentials only. Encrypted at rest, stored apart from other settings, never shown in reports or logs.', icon.lock)}</div>
				{#if credentialBadge}
					<span class="flex items-center gap-2 rounded px-2 py-1 text-[11px] {credentialBadge[1]}"><span class="size-1.5 rounded-full bg-current"></span>{credentialBadge[0]}</span>
				{/if}
			</div>
			<div class="space-y-6 p-6">
				{#if settings.schemes.length}
					<p class="flex gap-3 rounded-md border border-line p-4 text-xs text-slate-300">
						<span class="text-accent-ink">{@render svg(icon.info)}</span>
						<span>The spec declares {schemeSummary || 'no current schemes'}. OpenAPI describes how to authenticate but never contains working credentials.</span>
					</p>
					{#if !target}<p class="text-xs text-amber-300">Save a target API before adding credentials.</p>{/if}
					<div class="divide-y divide-line-faint [&>*]:py-6 [&>*:first-child]:pt-0 [&>*:last-child]:pb-0">
						{#each settings.schemes as scheme (scheme.name)}
							<SchemeCredential {scheme} {api} {canEdit} hasTarget={!!target} />
						{/each}
					</div>
				{:else}
					<p class="text-xs text-slate-500">The active specification declares no security schemes, so requests are sent without credentials.</p>
				{/if}
			</div>
		</section>

		<section id="headers" class="scroll-mt-6 rounded-lg border border-line">
			<div class="flex flex-wrap items-start justify-between gap-4 p-6">
				<div>{@render cardTitle('Global headers and test values', 'Sent with every scenario request. Values marked secret are encrypted like credentials.', icon.globe)}</div>
				{#if canEdit && !adding}
					<button type="button" onclick={() => ((adding = true), (headerError = ''))} disabled={!target} title={target ? undefined : 'Save a target API first'} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint disabled:opacity-50">+ Add header</button>
				{/if}
			</div>

			{#if adding}
				<form class="grid gap-4 border-t border-line-faint p-6 md:grid-cols-2" onsubmit={(e) => (e.preventDefault(), addHeader())}>
					<label class="block text-xs text-slate-400">Header<input bind:value={header.name} required maxlength="200" placeholder="X-Tenant-Id" class="auth-input mt-2" /></label>
					<label class="block text-xs text-slate-400">Value<input type={header.secret ? 'password' : 'text'} bind:value={header.value} required maxlength="4096" autocomplete="off" class="auth-input mt-2" /></label>
					<label class="block text-xs text-slate-400">Applied to (optional)
						<input bind:value={header.appliesTo} placeholder="POST /payments, POST /refunds" class="auth-input mt-2 font-mono" />
						<span class="mt-2 block text-[11px] text-slate-500">Comma-separated operations. Leave empty for all requests.</span>
					</label>
					<div class="flex items-center justify-between gap-4 self-start pt-6">
						<span class="text-[13px]">Secret<span class="block text-[11px] text-slate-500">Encrypted; only the last 4 characters are shown.</span></span>
						<Toggle checked={header.secret} label="Secret value" onchange={(on) => (header.secret = on)} />
					</div>
					<div class="flex justify-end gap-3 md:col-span-2">
						<button type="button" onclick={() => ((adding = false), (headerError = ''))} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint">Cancel</button>
						<button type="submit" class="h-9 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover">Add header</button>
					</div>
				</form>
			{/if}
			{#if headerError}<p class="px-6 pb-4 text-[13px] text-rose-300" role="alert">{headerError}</p>{/if}

			<div class="overflow-x-auto">
				<table class="w-full min-w-[44rem] text-left text-[13px]">
					<thead class="border-y border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
						<tr>
							{#each ['Header', 'Value', 'Secret', 'Applied to', ''] as heading, i (i)}
								<th scope="col" class="px-6 py-3 font-semibold">{heading}</th>
							{/each}
						</tr>
					</thead>
					<tbody class="divide-y divide-line-faint">
						{#each settings.headers as row (row.id)}
							<tr>
								<td class="px-6 py-4">{row.name}</td>
								<td class="px-6 py-4 font-mono text-xs text-slate-300">{row.secret ? `${'•'.repeat(12)} ${row.hint ?? ''}` : row.value}</td>
								<td class="px-6 py-4">
									<Toggle checked={row.secret} disabled={!canEdit || row.secret} label="Secret value for {row.name}" onchange={() => updateHeader(row, 'PATCH')} />
								</td>
								<td class="px-6 py-4 text-slate-300">{row.appliesTo.length ? row.appliesTo.join(', ') : 'All requests'}</td>
								<td class="px-6 py-4 text-right">
									{#if canEdit}
										<button type="button" onclick={() => updateHeader(row, 'DELETE')} aria-label="Remove header {row.name}" class="text-slate-400 hover:text-rose-300">{@render svg(icon.trash)}</button>
									{/if}
								</td>
							</tr>
						{:else}
							<tr><td colspan="5" class="px-6 py-8 text-center text-xs text-slate-500">No global headers.</td></tr>
						{/each}
					</tbody>
				</table>
			</div>
			<p class="px-6 py-4 text-[11px] text-slate-500">A secret value cannot be made plain again; remove the header and add it again instead.</p>
		</section>
	</div>
</div>
