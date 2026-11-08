<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { AuthType, SecurityScheme } from '$lib/api';
	import { dateTime } from '$lib/format';
	import { send } from '$lib/problem';

	let { scheme, api, canEdit, hasTarget }: { scheme: SecurityScheme; api: string; canEdit: boolean; hasTarget: boolean } = $props();

	const credential = $derived(scheme.credential);
	const url = $derived(`${api}/credentials/${encodeURIComponent(scheme.name)}`);
	const specType: AuthType = $derived(
		scheme.type === 'apiKey' ? 'ApiKey' : scheme.type === 'http' && scheme.httpScheme === 'basic' ? 'Basic' : 'Bearer'
	);
	const typeLabels: Record<AuthType, string> = { None: 'None', Bearer: 'Bearer token', ApiKey: 'API key', Basic: 'Basic' };

	// Local edits reset whenever the saved credential changes.
	let open = $derived(!!credential);
	let type = $derived<AuthType>(credential?.authType ?? specType);
	let label = $derived(credential?.label ?? '');
	let username = $derived(credential?.username ?? '');
	let secret = $state('');
	// A stored secret is replaced only on request; otherwise saving keeps it.
	let replacing = $derived(!credential?.hint || credential.authType !== type);
	let busy = $state(false);
	let error = $state('');

	const dirty = $derived(
		!credential ||
			type !== credential.authType ||
			label !== (credential.label ?? '') ||
			username !== (credential.username ?? '') ||
			secret !== ''
	);
	const location = $derived(
		scheme.type === 'apiKey' ? `${scheme.in ?? 'header'} ${scheme.parameterName ?? ''}`.trim() : scheme.httpScheme ? `HTTP ${scheme.httpScheme}` : scheme.type
	);
	const skipped = $derived(scheme.operations.slice(0, 2).join(', ') + (scheme.operations.length > 2 ? ` and ${scheme.operations.length - 2} more` : ''));

	async function act(run: () => Promise<string | null>) {
		busy = true;
		error = (await run()) ?? '';
		if (!error) {
			secret = '';
			await invalidateAll();
		}
		busy = false;
	}

	const save = () =>
		act(() => send(url, 'PUT', { authType: type, secret: secret || null, username: username || null, label: label || null }));
	const remove = () => act(() => send(url, 'DELETE'));
	// A failed validation can still record a result (e.g. an undecryptable secret), so reload either way.
	const validate = () =>
		act(async () => {
			const failed = await send(`${url}/validate`, 'POST');
			if (failed) await invalidateAll();
			return failed;
		});
</script>

<div class="space-y-4">
	<div class="flex flex-wrap items-start justify-between gap-4">
		<div>
			<p class="text-[13px]">Scheme: {scheme.name}{#if scheme.inSpec}<span class="text-slate-400"> ({location})</span>{/if}</p>
			<p class="mt-1 text-[11px] text-slate-500">
				{#if !scheme.inSpec}
					No longer in the active specification.
				{:else}
					Used by {scheme.operations.length} operation{scheme.operations.length === 1 ? '' : 's'}.{#if !credential && scheme.operations.length} Not configured, so {skipped} {scheme.operations.length === 1 ? 'is' : 'are'} skipped with a reason.{/if}
				{/if}
			</p>
		</div>
		{#if !open && canEdit}
			<button type="button" onclick={() => (open = true)} disabled={!hasTarget} title={hasTarget ? undefined : 'Save a target API first'} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint disabled:opacity-50">+ Add {typeLabels[specType]}</button>
		{/if}
	</div>

	{#if open}
		<form class="space-y-4" onsubmit={(e) => (e.preventDefault(), save())}>
			<div class="inline-flex rounded-md border border-line p-1" role="radiogroup" aria-label="Authentication type for {scheme.name}">
				{#each Object.entries(typeLabels) as [value, text] (value)}
					<button type="button" role="radio" aria-checked={type === value} disabled={!canEdit} onclick={() => (type = value as AuthType)}
						class="rounded px-3 py-1.5 text-[13px] {type === value ? 'bg-line-strong font-semibold text-slate-100' : 'text-slate-400 hover:text-slate-100'}">{text}</button>
				{/each}
			</div>

			{#if type === 'None'}
				<p class="text-xs text-slate-500">Requests to these operations are sent without credentials.</p>
			{:else}
				<div class="grid gap-4 md:grid-cols-[3fr_2fr]">
					{#if type === 'Basic'}
						<label class="block text-xs text-slate-400">Username
							<input bind:value={username} required autocomplete="off" disabled={!canEdit} class="auth-input mt-2" />
						</label>
					{/if}
					<div class="text-xs text-slate-400">
						<label for="secret-{scheme.name}">{type === 'Basic' ? 'Password' : typeLabels[type]}</label>
						{#if replacing}
							<input id="secret-{scheme.name}" type="password" bind:value={secret} required autocomplete="new-password" disabled={!canEdit} class="auth-input mt-2 font-mono" />
						{:else}
							<div class="auth-input mt-2 flex items-center justify-between gap-3">
								<span class="truncate font-mono text-slate-200">{'•'.repeat(24)}{credential?.hint}</span>
								{#if canEdit}<button type="button" onclick={() => (replacing = true)} class="shrink-0 text-accent-ink hover:text-accent-ink-strong">Replace</button>{/if}
							</div>
						{/if}
						<span class="mt-2 block text-[11px] text-slate-500">Stored encrypted. Only the last 4 characters are ever shown.</span>
					</div>
					{#if type !== 'Basic'}
						<label class="block text-xs text-slate-400">{type === 'Bearer' ? 'Token' : 'Key'} label (optional)
							<input bind:value={label} maxlength="100" disabled={!canEdit} placeholder="staging-ci-token" class="auth-input mt-2" />
						</label>
					{/if}
				</div>
			{/if}

			<div class="flex flex-wrap items-center justify-between gap-4">
				<p class="text-xs {credential?.valid === false ? 'text-rose-300' : 'text-slate-400'}">
					{#if credential?.validatedAt}
						{credential.valid ? '✓' : '✕'} Last validated {dateTime(credential.validatedAt)}. {credential.validation}
					{:else if credential && credential.authType !== 'None'}
						Not validated yet.
					{/if}
				</p>
				{#if canEdit}
					<div class="flex items-center gap-4">
						{#if credential}
							<button type="button" onclick={remove} disabled={busy} class="text-[13px] text-accent-ink hover:text-accent-ink-strong">Remove</button>
						{:else}
							<button type="button" onclick={() => (open = false)} class="text-[13px] text-slate-400 hover:text-slate-100">Cancel</button>
						{/if}
						{#if dirty}
							<button type="submit" disabled={busy} class="h-9 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60">Save credentials</button>
						{:else if credential && credential.authType !== 'None'}
							<button type="button" onclick={validate} disabled={busy} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint disabled:opacity-60">{busy ? 'Validating…' : 'Validate credentials'}</button>
						{/if}
					</div>
				{/if}
			</div>
			{#if error}<p class="text-[13px] text-rose-300" role="alert">{error}</p>{/if}
		</form>
	{/if}
</div>
