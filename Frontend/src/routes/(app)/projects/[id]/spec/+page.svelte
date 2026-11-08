<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { EndpointTesting, SpecImportReport, SpecSource } from '$lib/api';
	import { dateTime } from '$lib/format';

	let { data } = $props();
	const project = $derived(data.project);
	const spec = $derived(data.spec);
	const api = $derived(`/api/projects/${project.id}/spec`);
	// Viewers get a read-only page; the backend answers 403 regardless.
	const myRole = $derived(project.members.find((m) => m.userId === data.me.id)?.role);
	const canEdit = $derived(myRole === 'Owner' || myRole === 'Editor');

	// ---- Import ----
	const MAX_BYTES = 5 * 1024 * 1024;
	let importing = $state(false);
	const showImport = $derived(canEdit && (importing || !spec));
	let source = $state<SpecSource>('Url');
	let url = $state('');
	let pasted = $state('');
	let file = $state<File | null>(null);
	let dragging = $state(false);
	let busy = $state(false);
	let formError = $state('');
	let report = $state<SpecImportReport | null>(null);
	let fileInput = $state<HTMLInputElement>();

	const errors = $derived(report?.issues.filter((i) => i.severity === 'Error').length ?? 0);
	const warnings = $derived(report?.issues.filter((i) => i.severity === 'Warning').length ?? 0);

	function pick(files: FileList | null | undefined) {
		const chosen = files?.[0];
		if (!chosen) return;
		formError = '';
		if (!/\.(json|ya?ml)$/i.test(chosen.name)) formError = 'Choose a .json, .yaml, or .yml file.';
		else if (chosen.size > MAX_BYTES) formError = 'The file must be at most 5 MB.';
		else {
			file = chosen;
			source = 'FileUpload';
		}
	}

	async function submit() {
		formError = '';
		const body =
			source === 'Url'
				? { source, url: url.trim() }
				: source === 'Paste'
					? { source, content: pasted }
					: { source, content: await file?.text(), filename: file?.name };
		if (source === 'Url' && !body.url) return void (formError = 'Enter the URL of an OpenAPI document.');
		if (source !== 'Url' && !body.content?.trim())
			return void (formError = source === 'Paste' ? 'Paste an OpenAPI document.' : 'Choose a file to upload.');

		busy = true;
		try {
			const res = await fetch(api, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(body)
			});
			if (res.status === 201 || res.status === 422) {
				report = await res.json();
				if (res.status === 201) {
					await invalidateAll();
					importing = false;
					pageNo = 1;
				}
			} else if (res.status === 400) {
				const problem = await res.json().catch(() => null);
				formError = Object.values<string[]>(problem?.errors ?? {})[0]?.[0] ?? 'Check the document and try again.';
			} else if (res.status === 403) formError = 'Only project owners and editors can import a specification.';
			else if (res.status === 409) formError = 'Another import finished first. Reload to see it.';
			else formError = 'Import failed. Try again.';
		} catch {
			formError = 'Could not reach the server.';
		} finally {
			busy = false;
		}
	}

	function downloadReport() {
		if (!report) return;
		const link = document.createElement('a');
		link.href = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' }));
		link.download = `${(report.filename ?? 'openapi').replace(/\.\w+$/, '')}-validation.json`;
		link.click();
		URL.revokeObjectURL(link.href);
	}

	function startImport() {
		report = null;
		formError = '';
		importing = true;
	}

	// ---- Endpoints ----
	const PAGE_SIZE = 10;
	const testingLabels: Record<EndpointTesting, string> = { Included: 'Included', Excluded: 'Excluded', NeverTest: 'Never test' };
	const testingClass: Record<EndpointTesting, string> = {
		Included: 'bg-emerald-500/10 text-emerald-300',
		Excluded: 'bg-slate-500/10 text-slate-400',
		NeverTest: 'bg-rose-500/10 text-rose-300'
	};
	const methodClass = (method: string) =>
		({ GET: 'text-sky-300', POST: 'text-emerald-300', PUT: 'text-amber-300', PATCH: 'text-amber-300', DELETE: 'text-rose-400' })[method] ??
		'text-slate-400';

	let filter = $state<EndpointTesting | 'All'>('All');
	let search = $state('');
	let pageNo = $state(1);
	let saveError = $state('');
	// Optimistic testing choices, keyed by endpoint id, until the next reload.
	let overrides = $state<Record<string, EndpointTesting>>({});

	const endpoints = $derived((spec?.endpoints ?? []).map((e) => ({ ...e, testing: overrides[e.id] ?? e.testing })));
	const count = (testing: EndpointTesting) => endpoints.filter((e) => e.testing === testing).length;
	const matches = $derived.by(() => {
		const query = search.trim().toLowerCase();
		return endpoints.filter(
			(e) =>
				(filter === 'All' || e.testing === filter) &&
				(!query || e.path.toLowerCase().includes(query) || e.operationId?.toLowerCase().includes(query))
		);
	});
	const pages = $derived(Math.max(1, Math.ceil(matches.length / PAGE_SIZE)));
	const shown = $derived(matches.slice((Math.min(pageNo, pages) - 1) * PAGE_SIZE, Math.min(pageNo, pages) * PAGE_SIZE));
	const changed = $derived(spec?.changes ? spec.changes.added + spec.changes.modified + spec.changes.removed.length : 0);

	async function setTesting(id: string, testing: EndpointTesting) {
		const before = overrides[id];
		overrides[id] = testing;
		saveError = '';
		try {
			const res = await fetch(`${api}/endpoints/${id}`, {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ testing })
			});
			if (!res.ok) throw new Error(String(res.status));
		} catch (e) {
			if (before) overrides[id] = before;
			else delete overrides[id];
			saveError =
				e instanceof Error && e.message === '403'
					? 'Only project owners and editors can change testing.'
					: 'Could not save the testing choice. Try again.';
		}
	}

	const icon = {
		file: '<path d="M7 3h7l5 5v13H7z"/><path d="M14 3v5h5"/>',
		search: '<circle cx="11" cy="11" r="6"/><path d="M20 20l-4.5-4.5"/>',
		code: '<path d="M8 7l-5 5 5 5M16 7l5 5-5 5"/>',
		refresh: '<path d="M20 11a8 8 0 00-14.7-4.3L4 8M4 4v4h4M4 13a8 8 0 0014.7 4.3L20 16M20 20v-4h-4"/>',
		alert: '<path d="M12 4l9 16H3zM12 10v4M12 17h.01"/>',
		link: '<path d="M10 14a4 4 0 005.7 0l3-3a4 4 0 00-5.7-5.7l-1 1M14 10a4 4 0 00-5.7 0l-3 3a4 4 0 005.7 5.7l1-1"/>',
		upload: '<path d="M12 16V4M7 9l5-5 5 5M4 16v4h16v-4"/>',
		clipboard: '<rect x="6" y="4" width="12" height="17" rx="2"/><path d="M9 4V3h6v1"/>',
		check: '<path d="M5 12l5 5 9-10"/>',
		lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 018 0v3"/>',
		info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>'
	};
</script>

<svelte:head><title>API specification · {project.name} · Chaos Lab</title></svelte:head>

{#snippet svg(path: string, cls = 'size-4')}
	<svg viewBox="0 0 24 24" class={cls} fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{@html path}</svg>
{/snippet}

{#snippet panelTitle(title: string, subtitle: string, path: string, iconClass = 'text-slate-100')}
	<h3 class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(path, `size-4 ${iconClass}`)}{title}</h3>
	<p class="mt-1 text-xs text-slate-500">{subtitle}</p>
{/snippet}

{#snippet validation(r: SpecImportReport, imported: boolean)}
	<section class="mt-6 rounded-lg border border-line" aria-live="polite">
		<div class="flex flex-wrap items-start justify-between gap-4 p-6">
			<div>
				{@render panelTitle(
					imported ? 'Imported with warnings' : 'Validation result',
					[r.filename, r.openApiVersion && `OpenAPI ${r.openApiVersion}`, r.operationCount && `${r.operationCount} operations found`]
						.filter(Boolean)
						.join(' · ') || 'The document could not be imported.',
					icon.alert,
					errors ? 'text-rose-400' : 'text-amber-300'
				)}
			</div>
			<div class="flex gap-2 text-[11px]">
				{#if errors}<span class="rounded bg-rose-500/10 px-2 py-1 text-rose-300">● {errors} error{errors === 1 ? '' : 's'}</span>{/if}
				{#if warnings}<span class="rounded bg-amber-500/10 px-2 py-1 text-amber-300">● {warnings} warning{warnings === 1 ? '' : 's'}</span>{/if}
			</div>
		</div>
		<div class="overflow-x-auto">
			<table class="w-full min-w-[44rem] text-left text-[13px]">
				<thead class="border-y border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
					<tr>
						<th scope="col" class="w-28 px-6 py-3 font-semibold">Severity</th>
						<th scope="col" class="px-6 py-3 font-semibold">Location</th>
						<th scope="col" class="px-6 py-3 font-semibold">Message</th>
					</tr>
				</thead>
				<tbody class="divide-y divide-line-faint">
					{#each r.issues as issue, i (i)}
						<tr>
							<td class="px-6 py-4">
								<span class="rounded px-2 py-1 text-[11px] {issue.severity === 'Error' ? 'bg-rose-500/10 text-rose-300' : 'bg-amber-500/10 text-amber-300'}">{issue.severity}</span>
							</td>
							<td class="px-6 py-4 font-mono text-xs break-all">{issue.location}</td>
							<td class="px-6 py-4 text-slate-300">{issue.message}</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
		<div class="flex flex-wrap items-center justify-between gap-4 border-t border-line-faint p-6">
			<p class="text-xs text-slate-500">Errors block import. Fix the document and fetch again. Warnings don't block import.</p>
			<div class="flex gap-2">
				<button type="button" onclick={downloadReport} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint">Download report</button>
				{#if !imported}
					<button type="button" onclick={submit} disabled={busy} class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60">
						{@render svg(icon.refresh)}{source === 'Url' ? 'Fetch again' : 'Validate again'}
					</button>
				{/if}
			</div>
		</div>
	</section>
{/snippet}

{#if showImport}
	<div class="mt-8 flex flex-wrap items-start justify-between gap-4">
		<div>
			<h2 class="text-xl font-bold">Import API specification</h2>
			<p class="mt-1 text-[13px] text-slate-400">ChaosLab plans scenarios from your OpenAPI document. Testing still needs a running target API.</p>
		</div>
		{#if spec}
			<button type="button" onclick={() => (importing = false)} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint">Cancel</button>
		{:else}
			<a href="/projects/{project.id}" class="grid h-9 place-items-center rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint">Cancel</a>
		{/if}
	</div>

	<div class="mt-5 grid items-start gap-6 lg:grid-cols-[1.6fr_1fr]">
		<section class="rounded-lg border border-line">
			<div class="border-b border-line-faint p-6">
				{@render panelTitle('Source', 'OpenAPI 3.x in JSON or YAML. Swagger 2.0 is converted where possible.', icon.file)}
			</div>
			<form class="space-y-5 p-6" onsubmit={(e) => (e.preventDefault(), submit())}>
				<div role="tablist" aria-label="Import source" class="inline-flex rounded-md border border-line bg-field p-1 text-[13px]">
					{#each [{ value: 'Paste', label: 'Paste', path: icon.clipboard }, { value: 'FileUpload', label: 'Upload file', path: icon.upload }, { value: 'Url', label: 'From URL', path: icon.link }] as tab (tab.value)}
						<button
							type="button"
							role="tab"
							aria-selected={source === tab.value}
							onclick={() => ((source = tab.value as SpecSource), (formError = ''))}
							class="flex items-center gap-2 rounded px-3 py-1.5 {source === tab.value ? 'bg-line font-medium text-slate-100' : 'text-slate-400 hover:text-slate-100'}"
						>{@render svg(tab.path, 'size-3.5')}{tab.label}</button>
					{/each}
				</div>

				{#if source === 'Url'}
					<div>
						<label for="spec-url" class="text-xs text-slate-300">OpenAPI document URL</label>
						<div class="relative mt-2">
							<span class="pointer-events-none absolute inset-y-0 left-3 grid place-items-center text-slate-500">{@render svg(icon.link)}</span>
							<input id="spec-url" type="url" bind:value={url} placeholder="https://api.example.com/openapi.yaml" class="auth-input pl-9" aria-describedby="spec-url-help" />
						</div>
						<p id="spec-url-help" class="mt-2 text-[11px] text-slate-500">ChaosLab fetches the document once. It does not call any operation in it.</p>
					</div>
				{:else if source === 'Paste'}
					<div>
						<label for="spec-paste" class="text-xs text-slate-300">OpenAPI document</label>
						<textarea id="spec-paste" bind:value={pasted} rows="12" spellcheck="false" placeholder="openapi: 3.0.3" class="mt-2 block w-full rounded-md border border-line bg-field p-3 font-mono text-xs text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent"></textarea>
					</div>
				{/if}

				{#if source !== 'Paste'}
					<button
						type="button"
						onclick={() => fileInput?.click()}
						ondragover={(e) => (e.preventDefault(), (dragging = true))}
						ondragleave={() => (dragging = false)}
						ondrop={(e) => (e.preventDefault(), (dragging = false), pick(e.dataTransfer?.files))}
						class="grid w-full place-items-center gap-1 rounded-md border border-dashed px-4 py-8 text-center {dragging ? 'border-accent bg-accent-tint' : 'border-line-strong'}"
					>
						<span class="text-slate-400">{@render svg(icon.upload, 'size-5')}</span>
						{#if source === 'FileUpload' && file}
							<span class="text-[13px]">{file.name}</span>
							<span class="text-[11px] text-slate-500">{(file.size / 1024).toFixed(1)} KB · click or drop to replace</span>
						{:else}
							<span class="text-[13px]">{source === 'Url' ? 'Or drop' : 'Drop'} a .json / .yaml file here{source === 'FileUpload' ? ', or click to choose' : ''}</span>
							<span class="text-[11px] text-slate-500">Max 5 MB</span>
						{/if}
					</button>
					<input bind:this={fileInput} type="file" accept=".json,.yaml,.yml" class="hidden" onchange={(e) => pick(e.currentTarget.files)} />
				{/if}

				{#if formError}<p role="alert" class="text-xs text-rose-300">{formError}</p>{/if}

				<div class="flex flex-wrap items-center gap-6">
					<button type="submit" disabled={busy} class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60">
						{@render svg(icon.refresh, `size-4 ${busy ? 'animate-spin' : ''}`)}
						{busy ? 'Validating…' : source === 'Url' ? 'Fetch and validate' : 'Validate and import'}
					</button>
					{#if source !== 'Paste'}
						<button type="button" onclick={() => ((source = 'Paste'), (formError = ''))} class="text-[13px] text-accent-ink hover:text-accent-ink-strong">Paste document instead</button>
					{/if}
				</div>
			</form>
		</section>

		<section class="rounded-lg border border-line">
			<div class="border-b border-line-faint p-6">
				{@render panelTitle('What ChaosLab reads', 'Used to discover endpoints and plan scenarios.', icon.search, 'text-accent-ink')}
			</div>
			<div class="p-6">
				<ul class="space-y-3 text-[13px] text-slate-300">
					{#each ['Servers and base paths', 'Paths and HTTP methods', 'Parameters and request bodies', 'Response schemas and status codes', 'Security schemes, global and per operation', 'Tags and operation IDs'] as item (item)}
						<li class="flex items-center gap-3"><span class="text-emerald-400">{@render svg(icon.check)}</span>{item}</li>
					{/each}
				</ul>
				<p class="mt-5 flex gap-3 rounded-md border border-line bg-field p-4 text-xs text-slate-400">
					<span class="shrink-0 text-accent-ink">{@render svg(icon.lock)}</span>
					A spec never contains working credentials. Add test credentials under Settings → Credentials after import.
				</p>
			</div>
		</section>
	</div>

	{#if report?.issues.length}{@render validation(report, false)}{/if}
{:else if spec}
	<div class="mt-8 flex flex-wrap items-start justify-between gap-4">
		<div class="min-w-0">
			<h2 class="text-xl font-bold">API specification</h2>
			<p class="mt-1 truncate text-[13px] text-slate-400">
				{spec.filename ?? spec.sourceUrl ?? 'Pasted document'} · imported {dateTime(spec.importedAt)} by {spec.importedBy}
			</p>
		</div>
		<div class="flex gap-2">
			<a href="{api}/document" target="_blank" rel="noopener" class="flex h-9 items-center gap-2 rounded-md border border-line-strong px-4 text-[13px] hover:bg-line-faint">{@render svg(icon.code)}View document</a>
			{#if canEdit}
				<button type="button" onclick={startImport} class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover">{@render svg(icon.refresh)}Re-import</button>
			{/if}
		</div>
	</div>

	{#if spec.changes && changed}
		<p class="mt-5 flex items-center gap-3 rounded-lg border border-line bg-panel px-4 py-3 text-xs text-slate-300">
			<span class="shrink-0 text-accent-ink">{@render svg(icon.info)}</span>
			Last re-import changed {changed} operation{changed === 1 ? '' : 's'}: {spec.changes.added} added, {spec.changes.removed.length} removed, {spec.changes.modified} modified. Past runs keep the snapshots they ran with.
		</p>
	{/if}

	{#if report?.issues.length}{@render validation(report, true)}{/if}

	<dl class="mt-5 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line lg:grid-cols-5">
		{#each [
			{ label: 'OpenAPI version', value: spec.openApiVersion, note: 'Validated, no errors', noteClass: 'text-emerald-400' },
			{ label: 'Operations', value: endpoints.length, note: `${count('Included')} included in testing` },
			{ label: 'Security schemes', value: spec.securitySchemes.length, note: spec.securitySchemes.map((s) => (s.global ? `${s.name} (global)` : s.name)).join(', ') || 'None declared' },
			{ label: 'Excluded', value: count('Excluded'), note: 'Skipped in every run' },
			{ label: 'Never test', value: count('NeverTest'), note: 'Marked unsafe to call', valueClass: 'text-rose-400' }
		] as stat (stat.label)}
			<div class="min-w-0 bg-canvas p-5 first:col-span-2 lg:first:col-span-1">
				<dt class="text-xs text-slate-400">{stat.label}</dt>
				<dd class="mt-3 text-2xl font-semibold {stat.valueClass ?? ''}">{stat.value}</dd>
				<dd class="mt-2 truncate text-[11px] {stat.noteClass ?? 'text-slate-500'}" title={stat.note}>{stat.note}</dd>
			</div>
		{/each}
	</dl>

	<section class="mt-6 rounded-lg border border-line">
		<div class="flex flex-wrap items-start justify-between gap-4 p-6">
			<div>
				{@render panelTitle('Discovered endpoints', 'Choose what runs can touch. "Never test" operations are hidden from run setup.', icon.code, 'text-accent-ink')}
			</div>
			<div class="flex flex-wrap gap-3">
				<div class="relative w-64 max-w-full">
					<span class="pointer-events-none absolute inset-y-0 left-3 grid place-items-center text-slate-500">{@render svg(icon.search)}</span>
					<input type="search" bind:value={search} oninput={() => (pageNo = 1)} placeholder="Search path or operation ID" aria-label="Search path or operation ID" class="auth-input h-9 pl-9" />
				</div>
				<div role="group" aria-label="Filter by testing" class="inline-flex rounded-md border border-line bg-field p-1 text-xs">
					{#each [['All', 'All'], ['Included', 'Included'], ['Excluded', 'Excluded'], ['NeverTest', 'Never test']] as [value, label] (value)}
						<button
							type="button"
							aria-pressed={filter === value}
							onclick={() => ((filter = value as typeof filter), (pageNo = 1))}
							class="rounded px-3 py-1 {filter === value ? 'bg-line font-medium text-slate-100' : 'text-slate-400 hover:text-slate-100'}"
						>{label}</button>
					{/each}
				</div>
			</div>
		</div>

		{#if saveError}<p role="alert" class="px-6 pb-4 text-xs text-rose-300">{saveError}</p>{/if}

		<div class="overflow-x-auto">
			<table class="w-full min-w-[68rem] text-left text-[13px]">
				<thead class="border-y border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
					<tr>
						{#each ['Endpoint', 'Operation ID', 'Tag', 'Security', 'Request body', 'Responses', 'Scenarios', 'Testing'] as heading (heading)}
							<th scope="col" class="px-6 py-3 font-semibold">{heading}</th>
						{/each}
					</tr>
				</thead>
				<tbody class="divide-y divide-line-faint">
					{#each shown as endpoint (endpoint.id)}
						<tr>
							<td class="px-6 py-4">
								<span class="flex items-center gap-3">
									<span class="w-12 shrink-0 text-[11px] font-semibold {methodClass(endpoint.httpMethod)}">{endpoint.httpMethod}</span>
									<span class="font-mono text-xs">{endpoint.path}</span>
									{#if endpoint.change}
										<span class="rounded px-2 py-0.5 text-[10px] {endpoint.change === 'New' ? 'bg-emerald-500/10 text-emerald-300' : 'bg-amber-500/10 text-amber-300'}">{endpoint.change}</span>
									{/if}
								</span>
							</td>
							<td class="px-6 py-4 text-slate-300">{endpoint.operationId ?? '—'}</td>
							<td class="px-6 py-4 text-slate-300">{endpoint.tag ?? '—'}</td>
							<td class="px-6 py-4 text-slate-300">{endpoint.security.join(', ') || 'None'}</td>
							<td class="px-6 py-4 text-slate-300">{endpoint.requestBody ?? '—'}</td>
							<td class="px-6 py-4 text-slate-300">{endpoint.responses.join(', ') || '—'}</td>
							<td class="px-6 py-4 text-slate-300">{endpoint.scenarioCount}</td>
							<td class="px-6 py-4">
								<select
									value={endpoint.testing}
									disabled={!canEdit}
									title={canEdit ? undefined : 'Only project owners and editors can change testing.'}
									onchange={(e) => setTesting(endpoint.id, e.currentTarget.value as EndpointTesting)}
									aria-label="Testing for {endpoint.httpMethod} {endpoint.path}"
									class="h-7 rounded border-0 py-0 pr-7 pl-2 text-[11px] focus:ring-1 focus:ring-accent {testingClass[endpoint.testing]}"
								>
									{#each Object.entries(testingLabels) as [value, label] (value)}
										<option {value} class="bg-panel text-slate-100">● {label}</option>
									{/each}
								</select>
							</td>
						</tr>
					{:else}
						<tr><td colspan="8" class="px-6 py-10 text-center text-xs text-slate-500">No operations match.</td></tr>
					{/each}
				</tbody>
			</table>
		</div>

		<div class="flex flex-wrap items-center justify-between gap-4 border-t border-line-faint px-6 py-4">
			<p class="text-xs text-slate-500">
				Showing {shown.length} of {matches.length} operations
				{#if spec.changes?.removed.length}
					· {spec.changes.removed.length} removed in last re-import ({spec.changes.removed.join(', ')})
				{/if}
			</p>
			{#if pages > 1}
				<nav aria-label="Pagination" class="flex gap-1 text-xs">
					<button type="button" disabled={pageNo <= 1} onclick={() => pageNo--} aria-label="Previous page" class="grid size-8 place-items-center rounded border border-line-strong disabled:opacity-40">‹</button>
					{#each Array.from({ length: pages }, (_, i) => i + 1) as n (n)}
						<button type="button" onclick={() => (pageNo = n)} aria-current={n === pageNo ? 'page' : undefined} class="grid size-8 place-items-center rounded border {n === pageNo ? 'border-accent text-slate-100' : 'border-line-strong text-slate-400'}">{n}</button>
					{/each}
					<button type="button" disabled={pageNo >= pages} onclick={() => pageNo++} aria-label="Next page" class="grid size-8 place-items-center rounded border border-line-strong disabled:opacity-40">›</button>
				</nav>
			{/if}
		</div>
	</section>
{:else}
	<p class="mt-8 rounded-lg border border-line px-6 py-10 text-center text-[13px] text-slate-400">
		No API specification has been imported yet. Project owners and editors can import one.
	</p>
{/if}
