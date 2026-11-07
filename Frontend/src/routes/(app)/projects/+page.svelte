<script lang="ts">
	import { goto } from '$app/navigation';
	import type { ProjectListRow } from '$lib/api';
	import { PLACEHOLDER, relative, score, scoreLabel } from '$lib/format';

	let { data } = $props();

	type Status = 'Healthy' | 'Needs attention' | 'At risk' | 'Not tested';
	const statusOf = (p: ProjectListRow): Status =>
		p.latestScore == null
			? 'Not tested'
			: ({ Good: 'Healthy', Fair: 'Needs attention', Poor: 'At risk' } as const)[scoreLabel(p.latestScore)];
	const statusStyles: Record<Status, { text: string; pill: string; bar: string }> = {
		Healthy: { text: 'text-emerald-400', pill: 'bg-emerald-500/10 text-emerald-300', bar: 'bg-emerald-400' },
		'Needs attention': { text: 'text-amber-400', pill: 'bg-amber-500/10 text-amber-300', bar: 'bg-amber-400' },
		'At risk': { text: 'text-rose-400', pill: 'bg-rose-500/10 text-rose-300', bar: 'bg-rose-400' },
		'Not tested': { text: 'text-slate-500', pill: 'bg-slate-500/10 text-slate-400', bar: 'bg-slate-500' }
	};
	const tiles = [
		'bg-accent-tint text-accent-ink',
		'bg-chip-sky text-sky-300',
		'bg-chip-emerald text-emerald-300',
		'bg-chip-rose text-rose-300',
		'bg-chip-fuchsia text-fuchsia-300'
	];
	// Stable per project, independent of sort order.
	const tile = (id: string) => tiles[parseInt(id.slice(-4), 16) % tiles.length];

	let query = $state('');
	let status = $state<'all' | Status>('all');
	let sort = $state<'updated' | 'name' | 'score'>('updated');
	let pageNo = $state(1);
	const pageSize = 10;

	const activity = (p: ProjectListRow) => Date.parse(p.lastRunAt ?? p.updatedAt);
	const filtered = $derived.by(() => {
		const q = query.trim().toLowerCase();
		return data.projects
			.filter((p) => status === 'all' || statusOf(p) === status)
			.filter((p) => !q || [p.name, p.description, p.baseUrl].some((v) => v?.toLowerCase().includes(q)))
			.sort((a, b) =>
				sort === 'name'
					? a.name.localeCompare(b.name)
					: sort === 'score'
						? (b.latestScore ?? -1) - (a.latestScore ?? -1)
						: activity(b) - activity(a)
			);
	});
	const pageCount = $derived(Math.max(1, Math.ceil(filtered.length / pageSize)));
	const current = $derived(Math.min(pageNo, pageCount));
	const visible = $derived(filtered.slice((current - 1) * pageSize, current * pageSize));

	let search: HTMLInputElement;
	const modKey = /Mac|iPhone|iPad/.test(navigator.userAgent) ? '⌘' : 'Ctrl';
	function onkeydown(event: KeyboardEvent) {
		if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
			event.preventDefault();
			search.focus();
		}
	}

	let dialog: HTMLDialogElement;
	let name = $state('');
	let description = $state('');
	let createError = $state('');
	let creating = $state(false);

	function openCreate() {
		name = description = createError = '';
		dialog.showModal();
	}

	async function create(event: SubmitEvent) {
		event.preventDefault();
		createError = '';
		creating = true;
		try {
			const res = await fetch('/api/projects', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ name, description })
			});
			if (res.status === 400) {
				const problem = await res.json();
				createError = problem.errors?.name?.[0] ?? 'Check the project details and try again.';
				return;
			}
			if (!res.ok) throw new Error(`Create failed with ${res.status}`);
			const { id } = await res.json();
			dialog.close();
			await goto(`/projects/${id}`, { invalidateAll: true });
		} catch {
			createError = 'Could not create the project. Try again.';
		} finally {
			creating = false;
		}
	}
</script>

<svelte:head><title>Projects · Chaos Lab</title></svelte:head>
<svelte:window {onkeydown} />

<div class="flex flex-wrap items-end justify-between gap-6">
	<div>
		<p class="text-[10px] font-semibold tracking-wide text-slate-400 uppercase">Workspace / Inventory</p>
		<h1 class="mt-2 text-3xl font-bold tracking-tight">Projects</h1>
		<p class="mt-2 text-sm text-slate-400">Find, manage, and test the resilience of your API services.</p>
	</div>
	<button
		type="button"
		onclick={openCreate}
		class="flex h-9 items-center gap-2 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover"
	>
		<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
		Create project
	</button>
</div>

<div class="mt-6 flex flex-wrap gap-3">
	<label class="relative min-w-60 flex-1">
		<span class="sr-only">Search projects</span>
		<svg viewBox="0 0 24 24" class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate-500" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="6" /><path d="M20 20l-4.5-4.5" /></svg>
		<input
			bind:this={search}
			bind:value={query}
			oninput={() => (pageNo = 1)}
			type="search"
			placeholder="Search projects, APIs, or descriptions…"
			class="block h-10 w-full rounded-md border border-line bg-panel pr-16 pl-9 text-[13px] text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent"
		/>
		<kbd class="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 rounded border border-line-strong px-1.5 text-[10px] text-slate-500">{modKey} K</kbd>
	</label>
	<label>
		<span class="sr-only">Filter by status</span>
		<select bind:value={status} onchange={() => (pageNo = 1)} class="h-10 rounded-md border border-line bg-panel pr-9 text-[13px] text-slate-200 focus:border-accent focus:ring-1 focus:ring-accent">
			<option value="all">All statuses</option>
			{#each Object.keys(statusStyles) as option (option)}<option value={option}>{option}</option>{/each}
		</select>
	</label>
	<label>
		<span class="sr-only">Sort by</span>
		<select bind:value={sort} class="h-10 rounded-md border border-line bg-panel pr-9 text-[13px] text-slate-200 focus:border-accent focus:ring-1 focus:ring-accent">
			<option value="updated">Recently updated</option>
			<option value="name">Name</option>
			<option value="score">Resilience score</option>
		</select>
	</label>
</div>

<p class="mt-8 text-[11px] text-slate-500" aria-live="polite">{filtered.length} of {data.projects.length} projects</p>

{#if visible.length}
	<div class="mt-3 overflow-x-auto rounded-lg border border-line">
		<table class="w-full min-w-[60rem] text-left text-[13px]">
			<thead class="border-b border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
				<tr>
					{#each ['Project', 'Environment', 'Scenarios', 'Last run', 'Resilience score', 'Status'] as heading (heading)}
						<th scope="col" class="px-5 py-4 font-semibold">{heading}</th>
					{/each}
					<th scope="col" class="px-5 py-4"><span class="sr-only">Actions</span></th>
				</tr>
			</thead>
			<tbody class="divide-y divide-line-faint">
				{#each visible as project (project.id)}
					{@const projectStatus = statusOf(project)}
					{@const style = statusStyles[projectStatus]}
					<tr class="hover:bg-panel">
						<td class="px-5 py-5">
							<div class="flex items-center gap-3">
								<span class="grid size-9 shrink-0 place-items-center rounded-md font-mono text-xs {tile(project.id)}" aria-hidden="true">&lt;/&gt;</span>
								<div class="min-w-0">
									<a href="/projects/{project.id}" class="block truncate font-semibold hover:text-strong">{project.name}</a>
									{#if project.description}<p class="truncate text-[11px] text-slate-400">{project.description}</p>{/if}
									{#if project.baseUrl}<p class="truncate font-mono text-[10px] text-slate-500">{project.baseUrl.replace(/^https?:\/\//, '')}</p>{/if}
								</div>
							</div>
						</td>
						<td class="px-5 py-5 text-slate-400">
							{#if project.environmentName}
								<span class="flex items-center gap-2"><span class="size-2 rounded-full border border-slate-400" aria-hidden="true"></span>{project.environmentName}</span>
							{:else}{PLACEHOLDER}{/if}
						</td>
						<td class="px-5 py-5 text-slate-300">{project.scenarioCount}</td>
						<td class="px-5 py-5 text-slate-400">{project.lastRunAt ? relative(project.lastRunAt) : 'Never'}</td>
						<td class="px-5 py-5">
							<span class="flex items-center gap-3">
								<span class="w-16 font-semibold {style.text}">{score(project.latestScore)}{#if project.latestScore != null}<span class="ml-0.5">/100</span>{/if}</span>
								<span class="h-1 w-12 overflow-hidden rounded-full bg-line" aria-hidden="true">
									<span class="block h-full rounded-full {style.bar}" style:width="{project.latestScore ?? 0}%"></span>
								</span>
							</span>
						</td>
						<td class="px-5 py-5">
							<span class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium whitespace-nowrap {style.pill}">
								<span class="size-1.5 rounded-full bg-current" aria-hidden="true"></span>{projectStatus}
							</span>
						</td>
						<td class="px-5 py-5 text-right">
							<button type="button" disabled title="Project actions are not available yet" aria-label="Actions for {project.name}" class="text-slate-600">⋯</button>
						</td>
					</tr>
				{/each}
			</tbody>
		</table>
	</div>

	<div class="mt-6 flex items-center justify-between text-[11px] text-slate-500">
		<p>Showing {visible.length} item{visible.length === 1 ? '' : 's'}</p>
		<nav aria-label="Pagination" class="flex gap-2">
			<button type="button" aria-label="Previous page" disabled={current === 1} onclick={() => (pageNo = current - 1)} class="grid size-8 place-items-center rounded-md border border-line hover:text-slate-200 disabled:opacity-40">‹</button>
			{#each { length: pageCount }, i (i)}
				<button
					type="button"
					aria-current={i + 1 === current ? 'page' : undefined}
					onclick={() => (pageNo = i + 1)}
					class={['grid size-8 place-items-center rounded-md border', i + 1 === current ? 'border-line-strong bg-field text-slate-100' : 'border-line hover:text-slate-200']}
				>{i + 1}</button>
			{/each}
			<button type="button" aria-label="Next page" disabled={current === pageCount} onclick={() => (pageNo = current + 1)} class="grid size-8 place-items-center rounded-md border border-line hover:text-slate-200 disabled:opacity-40">›</button>
		</nav>
	</div>
{:else}
	<p class="mt-3 grid min-h-40 place-items-center rounded-lg border border-dashed border-line px-4 text-center text-xs text-slate-500">
		{data.projects.length ? 'No projects match your search or filters.' : 'No projects yet. Create one to import an OpenAPI spec.'}
	</p>
{/if}

<dialog
	bind:this={dialog}
	aria-labelledby="create-title"
	class="m-auto w-full max-w-md rounded-lg border border-line bg-canvas p-6 text-slate-100 backdrop:bg-black/60"
>
	<h2 id="create-title" class="text-lg font-semibold">Create project</h2>
	<p class="mt-1 text-xs text-slate-400">You'll be the project owner. Import an OpenAPI spec next.</p>
	<form onsubmit={create} class="mt-6 space-y-5">
		<div>
			<label for="project-name" class="mb-2 block text-xs font-medium text-slate-200">Name</label>
			<input id="project-name" bind:value={name} required maxlength="200" placeholder="Payment Gateway" class="auth-input" />
		</div>
		<div>
			<label for="project-description" class="mb-2 block text-xs font-medium text-slate-200">Description <span class="text-slate-500">(optional)</span></label>
			<textarea id="project-description" bind:value={description} rows="3" class="auth-input h-auto py-2"></textarea>
		</div>
		{#if createError}<p class="text-xs text-rose-400" role="alert">{createError}</p>{/if}
		<div class="flex justify-end gap-3">
			<button type="button" onclick={() => dialog.close()} class="h-9 rounded-md border border-line-strong px-4 text-[13px] hover:bg-field">Cancel</button>
			<button type="submit" disabled={creating} class="h-9 rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60">
				{creating ? 'Creating…' : 'Create project'}
			</button>
		</div>
	</form>
</dialog>
