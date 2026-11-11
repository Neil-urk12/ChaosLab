<script lang="ts">
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { SvelteSet } from 'svelte/reactivity';
	import Toggle from '$lib/components/Toggle.svelte';
	import type { RunPreflight, Scenario, ScenarioType } from '$lib/api';
	import { relative } from '$lib/format';
	import { problemMessage } from '$lib/problem';
	import { canStartRuns, confirmationRows } from '$lib/runs';
	import { categories, faultLabels, isStateChanging, PRIMITIVES } from '$lib/scenarios';

	let { data } = $props();
	const project = $derived(data.project);
	const base = $derived(`/projects/${project.id}`);
	// Viewers can't start runs; the backend answers 403 regardless.
	const canRun = $derived(canStartRuns(project, data.me));
	const scenarios = $derived<Scenario[]>(data.list?.scenarios ?? []);

	const icon = {
		code: '<path d="M8 8l-4 4 4 4M16 8l4 4-4 4"/>',
		bolt: '<path d="M13 3L5 14h6l-1 7 8-11h-6z"/>',
		sliders: '<path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0"/><circle cx="16" cy="6" r="2"/><circle cx="10" cy="12" r="2"/><circle cx="18" cy="18" r="2"/>',
		target: '<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="4"/>',
		play: '<path d="M7 5v14l11-7z"/>',
		alert: '<path d="M12 4l9 16H3zM12 10v4M12 17h.01"/>',
		check: '<path d="M5 12l5 5 9-10"/>',
		close: '<path d="M6 6l12 12M18 6L6 18"/>'
	};
	const methodClass = (method: string) =>
		({ GET: 'text-sky-300', POST: 'text-emerald-300', PUT: 'text-amber-300', PATCH: 'text-amber-300', DELETE: 'text-rose-400' })[method] ??
		'text-slate-400';
	const checkboxClass = 'size-4 rounded border-line-strong bg-field text-accent focus:ring-accent focus:ring-offset-0';

	// ---- Preflight ----
	let preflight = $state<RunPreflight | null>(null);
	let checking = $state(false);
	let preflightError = $state('');

	async function check() {
		checking = true;
		preflightError = '';
		try {
			const res = await fetch(`/api/projects/${project.id}/runs/preflight`);
			if (res.ok) preflight = await res.json();
			else preflightError = await problemMessage(res);
		} catch {
			preflightError = 'Could not reach the server.';
		} finally {
			checking = false;
		}
	}
	onMount(() => {
		if (canRun) check();
	});

	type Check = { label: string; detail: string; state: 'ok' | 'warn' | 'fail' };
	const checks = $derived.by((): Check[] => {
		if (!preflight) return [];
		const { target, reachability, credentials, spec } = preflight;
		const reach: Check = !target
			? { label: 'No target', detail: 'Configure one in Settings', state: 'fail' }
			: reachability?.reachable
				? { label: 'Target reachable', detail: `GET / returned ${reachability.status} in ${reachability.elapsedMs} ms`, state: 'ok' }
				: { label: 'Target unreachable', detail: reachability?.error ?? 'No response', state: 'fail' };
		const problems = credentials.filter((c) => !c.configured || c.valid !== true);
		const validated = credentials.map((c) => c.validatedAt).filter((v): v is string => !!v).sort()[0];
		const creds: Check = !credentials.length
			? { label: 'No credentials needed', detail: 'Included operations use no security scheme', state: 'ok' }
			: !problems.length
				? { label: 'Credentials valid', detail: `${credentials.map((c) => c.scheme).join(', ')} validated ${validated ? relative(validated) : ''}`, state: 'ok' }
				: {
						label: 'Check credentials',
						detail: problems.map((c) => `${c.scheme}: ${!c.configured ? 'not configured' : c.valid === false ? 'last validation failed' : 'not validated'}`).join(' · '),
						state: 'warn'
					};
		const specCheck: Check = spec
			? { label: 'Spec validated', detail: `OpenAPI ${spec.openApiVersion} · ${spec.operationCount} operations`, state: 'ok' }
			: { label: 'No specification', detail: 'Import one on the API specification tab', state: 'fail' };
		return [reach, creds, specCheck, { label: `Scoring version ${preflight.scoringVersion}`, detail: 'Locked for this run', state: 'ok' }];
	});

	// ---- Endpoints ----
	const ENDPOINT_PAGE = 8;
	let showAllEndpoints = $state(false);
	// Deselected rather than selected endpoints, so everything starts selected.
	const excluded = new SvelteSet<string>();
	const endpoints = $derived.by(() => {
		const map = new Map<string, { id: string; httpMethod: string; path: string }>();
		for (const s of scenarios) if (!map.has(s.endpointId)) map.set(s.endpointId, { id: s.endpointId, httpMethod: s.httpMethod, path: s.path });
		return [...map.values()];
	});
	const shownEndpoints = $derived(showAllEndpoints ? endpoints : endpoints.slice(0, ENDPOINT_PAGE));

	// ---- Scenarios ----
	// Enabled defaults follows each scenario's toggle on the Scenarios tab; choosing individually starts from those defaults.
	let mode = $state<'defaults' | 'individual'>('defaults');
	const picked = new SvelteSet<string>();
	const chosen = (s: Scenario) => (mode === 'defaults' ? s.enabled : picked.has(s.id));
	const selected = $derived(scenarios.filter((s) => !excluded.has(s.endpointId) && chosen(s)));
	const perEndpoint = $derived.by(() => {
		const counts = new Map<string, number>();
		for (const s of scenarios) if (chosen(s)) counts.set(s.endpointId, (counts.get(s.endpointId) ?? 0) + 1);
		return counts;
	});

	function chooseIndividually() {
		if (mode === 'individual') return;
		picked.clear();
		for (const s of scenarios) if (s.enabled) picked.add(s.id);
		mode = 'individual';
	}

	const ORDER: ScenarioType[] = ['Baseline', ...PRIMITIVES, 'DuplicateRequest'];
	const groups = $derived(
		ORDER.map((type) => {
			const all = scenarios.filter((s) => s.type === type && !excluded.has(s.endpointId));
			return { type, all, on: all.filter(chosen).length };
		}).filter((g) => g.all.length)
	);
	let expanded = $state<ScenarioType | null>(null);

	function toggleGroup(all: Scenario[], on: boolean) {
		chooseIndividually();
		for (const s of all) {
			if (on) picked.add(s.id);
			else picked.delete(s.id);
		}
	}
	function toggleScenario(s: Scenario, on: boolean) {
		chooseIndividually();
		if (on) picked.add(s.id);
		else picked.delete(s.id);
	}

	// ---- Options, confirmation and start ----
	let concurrency = $state(1);
	let baseline = $state(true);
	let retries = $state(2);
	let confirmed = $state(false);
	let starting = $state(false);
	let startError = $state('');

	const confirmRows = $derived(confirmationRows(selected));
	const toConfirm = $derived(confirmRows.reduce((n, r) => n + r.count, 0));
	const duplicates = $derived(confirmRows.reduce((n, r) => n + r.duplicates, 0));
	const endpointCount = $derived(new Set(selected.map((s) => s.endpointId)).size);
	const blocked = $derived(!preflight || preflight.blockers.length > 0);
	const ready = $derived(canRun && !blocked && !checking && selected.length > 0 && (toConfirm === 0 || confirmed) && !starting);
	const notReady = $derived(
		!canRun
			? 'Only project owners and editors can start runs.'
			: checking || !preflight
				? 'Waiting for the preflight checks.'
				: blocked
					? 'Resolve the preflight problems first.'
					: !selected.length
						? 'Select at least one scenario.'
						: toConfirm && !confirmed
							? 'Confirm the state-changing scenarios first.'
							: ''
	);

	async function start() {
		starting = true;
		startError = '';
		try {
			const res = await fetch(`/api/projects/${project.id}/runs`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					scenarioIds: selected.map((s) => s.id),
					concurrency,
					baselineRequests: baseline,
					runnerRetries: retries,
					confirmStateChanging: confirmed
				})
			});
			if (!res.ok) {
				startError = await problemMessage(res);
				return;
			}
			const { id } = await res.json();
			await goto(`${base}/runs/${id}`, { invalidateAll: true });
		} catch {
			startError = 'Could not reach the server.';
		} finally {
			starting = false;
		}
	}
</script>

{#snippet svg(path: string, cls = 'size-4')}
	<svg viewBox="0 0 24 24" class={cls} fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{@html path}</svg>
{/snippet}

<svelte:head><title>New test run · {project.name} · Chaos Lab</title></svelte:head>

<div class="mt-8 flex flex-wrap items-start justify-between gap-4">
	<div>
		<h2 class="text-lg font-semibold">New test run</h2>
		<p class="mt-1 text-xs text-slate-400">
			{project.name}{#if project.defaultEnvironment} · {project.defaultEnvironment.name} · {project.defaultEnvironment.baseUrl}{/if}
		</p>
	</div>
	<a href="{base}/runs" class="grid h-9 place-items-center rounded-md border border-line-strong px-4 text-[13px] text-slate-200 hover:bg-line-faint">Cancel</a>
</div>

{#if !canRun}
	<p class="mt-6 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-[13px] text-slate-500">
		Only project owners and editors can start runs.
	</p>
{:else if !data.list || !scenarios.length}
	<p class="mt-6 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-[13px] text-slate-500">
		<span>
			{#if !data.list}
				No specification yet. <a href="{base}/spec" class="text-accent-ink hover:text-accent-ink-strong">Import an OpenAPI document</a> first.
			{:else}
				No scenarios yet. <a href="{base}/scenarios" class="text-accent-ink hover:text-accent-ink-strong">Generate scenarios</a> first.
			{/if}
		</span>
	</p>
{:else}
	<section aria-label="Preflight" class="mt-6 rounded-lg border border-line p-5">
		<div class="flex flex-wrap items-center gap-x-8 gap-y-4">
			<p class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Preflight</p>
			{#if checking}
				<p class="text-[13px] text-slate-400" role="status">Checking the target…</p>
			{:else if preflightError}
				<p class="text-[13px] text-rose-300" role="alert">{preflightError}</p>
			{:else}
				<ul class="grid flex-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
					{#each checks as item (item.label)}
						<li class="flex min-w-0 gap-3">
							<span class="grid size-6 shrink-0 place-items-center rounded {item.state === 'ok' ? 'bg-chip-emerald text-emerald-300' : item.state === 'warn' ? 'bg-amber-500/15 text-amber-300' : 'bg-chip-rose text-rose-300'}">
								{@render svg(item.state === 'ok' ? icon.check : item.state === 'warn' ? icon.alert : icon.close, 'size-3.5')}
							</span>
							<span class="min-w-0">
								<span class="block text-[13px]">{item.label}</span>
								<span class="block truncate text-[11px] text-slate-500" title={item.detail}>{item.detail}</span>
							</span>
						</li>
					{/each}
				</ul>
			{/if}
			<button type="button" onclick={check} disabled={checking} class="text-xs text-accent-ink hover:text-accent-ink-strong disabled:opacity-50">Check again</button>
		</div>
		{#if preflight?.blockers.length}
			<ul class="mt-4 space-y-1 rounded-md border border-danger-line bg-danger-panel p-4 text-xs text-rose-300" role="alert">
				{#each preflight.blockers as blocker (blocker)}<li>{blocker}</li>{/each}
			</ul>
		{/if}
	</section>

	<div class="mt-6 grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_28rem]">
		<div class="min-w-0 space-y-6">
			<section class="rounded-lg border border-line" aria-labelledby="endpoints-heading">
				<div class="flex flex-wrap items-start justify-between gap-4 border-b border-line-faint p-5">
					<div>
						<h3 id="endpoints-heading" class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(icon.code, 'size-4 text-accent-ink')}Endpoints</h3>
						<p class="mt-1 text-xs text-slate-500">
							{endpoints.length - excluded.size} of {endpoints.length} endpoints with scenarios selected{#if preflight?.spec?.neverTestCount} · {preflight.spec.neverTestCount} "Never test" operation{preflight.spec.neverTestCount === 1 ? ' is' : 's are'} hidden{/if}
						</p>
					</div>
					<div class="flex gap-4 text-[13px]">
						<button type="button" onclick={() => excluded.clear()} class="text-accent-ink hover:text-accent-ink-strong">Select all</button>
						<button type="button" onclick={() => endpoints.forEach((e) => excluded.add(e.id))} class="text-accent-ink hover:text-accent-ink-strong">Clear</button>
					</div>
				</div>
				<ul class="divide-y divide-line-faint">
					{#each shownEndpoints as endpoint (endpoint.id)}
						<li>
							<label class="flex cursor-pointer items-center gap-4 px-5 py-3.5 text-[13px] hover:bg-line-faint/50">
								<input
									type="checkbox"
									checked={!excluded.has(endpoint.id)}
									onchange={(e) => (e.currentTarget.checked ? excluded.delete(endpoint.id) : excluded.add(endpoint.id))}
									class={checkboxClass}
								/>
								<span class="w-12 text-[11px] font-semibold {methodClass(endpoint.httpMethod)}">{endpoint.httpMethod}</span>
								<span class="min-w-0 truncate font-mono text-xs">{endpoint.path}</span>
								{#if isStateChanging(endpoint.httpMethod)}
									<span class="rounded bg-amber-500/15 px-2 py-0.5 text-[11px] text-amber-300">State-changing</span>
								{/if}
								<span class="ml-auto shrink-0 text-xs text-slate-500">{perEndpoint.get(endpoint.id) ?? 0} scenario{perEndpoint.get(endpoint.id) === 1 ? '' : 's'}</span>
							</label>
						</li>
					{/each}
				</ul>
				{#if endpoints.length > ENDPOINT_PAGE}
					<div class="border-t border-line-faint p-4 text-center text-[13px]">
						<button type="button" onclick={() => (showAllEndpoints = !showAllEndpoints)} class="text-accent-ink hover:text-accent-ink-strong">
							{showAllEndpoints ? 'Show fewer endpoints' : `Show ${endpoints.length - ENDPOINT_PAGE} more endpoints`}
						</button>
					</div>
				{/if}
			</section>

			<section class="rounded-lg border border-line" aria-labelledby="scenarios-heading">
				<div class="flex flex-wrap items-start justify-between gap-4 border-b border-line-faint p-5">
					<div>
						<h3 id="scenarios-heading" class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(icon.bolt, 'size-4 text-accent-ink')}Scenarios</h3>
						<p class="mt-1 text-xs text-slate-500">{selected.length} {mode === 'defaults' ? 'enabled' : 'chosen'} scenarios for the selected endpoints, grouped by fault</p>
					</div>
					<div class="flex rounded-md border border-line-strong p-0.5 text-xs" role="group" aria-label="Scenario selection">
						<button type="button" aria-pressed={mode === 'defaults'} onclick={() => (mode = 'defaults')} class="rounded px-3 py-1.5 {mode === 'defaults' ? 'bg-line-strong text-slate-100' : 'text-slate-400 hover:text-slate-100'}">Enabled defaults</button>
						<button type="button" aria-pressed={mode === 'individual'} onclick={chooseIndividually} class="rounded px-3 py-1.5 {mode === 'individual' ? 'bg-line-strong text-slate-100' : 'text-slate-400 hover:text-slate-100'}">Choose individually</button>
					</div>
				</div>
				<ul class="divide-y divide-line-faint">
					{#each groups as group (group.type)}
						<li>
							<div class="flex items-center gap-4 px-5 py-3.5 text-[13px]">
								<input
									type="checkbox"
									aria-label="Run every {faultLabels[group.type]} scenario"
									checked={group.on === group.all.length}
									indeterminate={group.on > 0 && group.on < group.all.length}
									onchange={(e) => toggleGroup(group.all, e.currentTarget.checked)}
									class={checkboxClass}
								/>
								<button
									type="button"
									aria-expanded={expanded === group.type}
									onclick={() => (expanded = expanded === group.type ? null : group.type)}
									class="grid flex-1 grid-cols-[10rem_1fr_auto] items-center gap-4 text-left"
								>
									<span>{faultLabels[group.type]}</span>
									<span class="truncate text-xs text-slate-500">{categories[group.type]}</span>
									<span class="text-xs text-slate-500">{group.on === group.all.length ? `${group.on} scenario${group.on === 1 ? '' : 's'}` : `${group.on} of ${group.all.length}`}</span>
								</button>
							</div>
							{#if expanded === group.type}
								<ul class="border-t border-line-faint bg-panel py-1">
									{#each group.all as scenario (scenario.id)}
										<li>
											<label class="flex cursor-pointer items-center gap-4 py-2 pr-5 pl-12 text-xs hover:bg-line-faint/50">
												<input type="checkbox" checked={chosen(scenario)} onchange={(e) => toggleScenario(scenario, e.currentTarget.checked)} class={checkboxClass} />
												<span class="w-12 text-[11px] font-semibold {methodClass(scenario.httpMethod)}">{scenario.httpMethod}</span>
												<span class="min-w-0 truncate font-mono">{scenario.path}</span>
												{#if !scenario.enabled}<span class="ml-auto shrink-0 text-slate-500">Off by default</span>{/if}
											</label>
										</li>
									{/each}
								</ul>
							{/if}
						</li>
					{:else}
						<li class="px-5 py-10 text-center text-xs text-slate-500">Select at least one endpoint.</li>
					{/each}
				</ul>
			</section>
		</div>

		<div class="space-y-6">
			<section class="rounded-lg border border-line" aria-labelledby="options-heading">
				<div class="border-b border-line-faint p-5">
					<h3 id="options-heading" class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(icon.sliders, 'size-4 text-accent-ink')}Options</h3>
					<p class="mt-1 text-xs text-slate-500">Saved with this run's configuration snapshot.</p>
				</div>
				<div class="divide-y divide-line-faint px-5">
					<div class="py-5">
						<p id="concurrency-label" class="text-xs text-slate-300">Concurrent experiments</p>
						<div class="mt-3 inline-flex rounded-md border border-line-strong p-0.5 text-xs" role="radiogroup" aria-labelledby="concurrency-label">
							{#each [1, 2, 3, 4, 5] as n (n)}
								<button type="button" role="radio" aria-checked={concurrency === n} onclick={() => (concurrency = n)} class="w-10 rounded py-1.5 {concurrency === n ? 'bg-line-strong text-slate-100' : 'text-slate-400 hover:text-slate-100'}">{n}</button>
							{/each}
						</div>
						<p class="mt-2 text-[11px] text-slate-500">One at a time keeps timing measurements clean. You can raise it to five.</p>
					</div>
					<div class="flex items-start justify-between gap-4 py-5">
						<div>
							<p class="text-[13px]">Baseline requests</p>
							<p class="mt-1 text-[11px] text-slate-500">Send a normal request first where a scenario needs one.</p>
						</div>
						<Toggle checked={baseline} label="Baseline requests" onchange={(on) => (baseline = on)} />
					</div>
					<label class="block py-5 text-xs text-slate-300">
						Automatic retries for runner errors
						<select bind:value={retries} class="auth-input mt-3 w-32">
							{#each [0, 1, 2, 3] as n (n)}<option value={n}>{n}</option>{/each}
						</select>
						<span class="mt-2 block text-[11px] text-slate-500">Only ChaosLab's own runner or injector errors are retried. Target failures are results and are never retried.</span>
					</label>
				</div>
			</section>

			{#if toConfirm}
				<section class="rounded-lg border border-line" aria-labelledby="confirm-heading">
					<div class="border-b border-line-faint p-5">
						<h3 id="confirm-heading" class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(icon.alert, 'size-4 text-amber-300')}Confirm state-changing scenarios</h3>
						<p class="mt-1 text-xs text-slate-500">Required before this run can start.</p>
					</div>
					<div class="space-y-4 p-5 text-[13px]">
						<p class="text-slate-300">
							{toConfirm} scenario{toConfirm === 1 ? '' : 's'} call <strong class="text-slate-100">POST, PUT, PATCH or DELETE</strong>
							or send <strong class="text-slate-100">duplicate requests</strong>{#if duplicates} ({duplicates} duplicate){/if}. They can create records in the target environment.
						</p>
						<ul class="space-y-1.5 text-xs">
							{#each confirmRows as row (`${row.httpMethod} ${row.path}`)}
								<li class="flex gap-3">
									<span class="w-12 shrink-0 text-[11px] font-semibold {methodClass(row.httpMethod)}">{row.httpMethod}</span>
									<span class="min-w-0 truncate font-mono">{row.path}</span>
									<span class="ml-auto shrink-0 text-slate-500">{row.count}{#if row.duplicates} · incl. {row.duplicates} duplicate{/if}</span>
								</li>
							{/each}
						</ul>
						<label class="flex cursor-pointer gap-3 rounded-md border border-amber-400/30 bg-amber-500/10 p-4 text-xs text-amber-100">
							<input type="checkbox" bind:checked={confirmed} class="{checkboxClass} mt-0.5" />
							<span><strong>I understand.</strong> This target uses test data only and these records can be created.</span>
						</label>
					</div>
				</section>
			{/if}

			<section class="rounded-lg border border-line" aria-labelledby="summary-heading">
				<div class="border-b border-line-faint p-5">
					<h3 id="summary-heading" class="flex items-center gap-2 text-[15px] font-semibold">{@render svg(icon.target, 'size-4 text-accent-ink')}Run summary</h3>
				</div>
				<div class="p-5">
					<dl class="space-y-3 text-[13px]">
						<div class="flex justify-between"><dt class="text-slate-400">Endpoints</dt><dd>{endpointCount}</dd></div>
						<div class="flex justify-between"><dt class="text-slate-400">Scenarios</dt><dd>{selected.length}</dd></div>
						<div class="flex justify-between">
							<dt class="text-slate-400">State-changing</dt>
							<dd class={toConfirm ? 'text-amber-300' : ''}>{toConfirm ? `${toConfirm} ${confirmed ? 'confirmed' : 'to confirm'}` : 'None'}</dd>
						</div>
						<div class="flex justify-between"><dt class="text-slate-400">Concurrency</dt><dd>{concurrency}</dd></div>
						<div class="flex justify-between"><dt class="text-slate-400">Scoring version</dt><dd>{preflight?.scoringVersion ?? '—'}</dd></div>
					</dl>
					<button
						type="button"
						onclick={start}
						disabled={!ready}
						title={notReady || undefined}
						class="mt-6 flex h-10 w-full items-center justify-center gap-2 rounded-md bg-accent text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
					>
						{@render svg(icon.play)}{starting ? 'Starting…' : 'Start run'}
					</button>
					{#if startError}
						<p class="mt-3 text-center text-xs text-rose-300" role="alert">{startError}</p>
					{:else if notReady}
						<p class="mt-3 text-center text-[11px] text-slate-500">{notReady}</p>
					{:else}
						<p class="mt-3 text-center text-[11px] text-slate-500">If you cancel, collected evidence is kept but the run isn't scored as complete.</p>
					{/if}
				</div>
			</section>
		</div>
	</div>
{/if}
