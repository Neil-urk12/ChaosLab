<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import type { MemberCandidate, ProjectMemberRow, ProjectRole } from '$lib/api';
	import { initials } from '$lib/format';
	import { send } from '$lib/problem';

	let { data } = $props();
	const project = $derived(data.project);
	const team = $derived(data.team);
	const canManage = $derived(team.yourRole === 'Owner');
	const api = $derived(`/api/projects/${project.id}/members`);

	const ROLES: { role: ProjectRole; text: string }[] = [
		{ role: 'Owner', text: 'Everything an Editor can do, plus manage members and delete the project.' },
		{ role: 'Editor', text: 'Configure the target, import specs, edit scenarios and invariants, start and cancel runs.' },
		{ role: 'Viewer', text: "Read results, findings and reports. Can't start runs or change settings." }
	];
	const avatarTones = ['bg-accent-tint text-accent-ink', 'bg-chip-emerald text-emerald-300', 'bg-chip-sky text-sky-300', 'bg-chip-fuchsia text-fuchsia-300', 'bg-amber-500/10 text-amber-300'];
	const avatarTone = (id: string) => avatarTones[parseInt(id.slice(0, 2), 16) % avatarTones.length];
	const day = (iso: string) => new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });

	let error = $state<string | null>(null);
	let busy = $state(false);
	async function run(url: string, method: string, body?: unknown) {
		busy = true;
		error = await send(url, method, body);
		// Reload on failure too, so the page shows what is actually stored.
		await invalidateAll();
		busy = false;
		return !error;
	}

	let query = $state('');
	const visible = $derived(
		team.members.filter((m) => `${m.displayName} ${m.email ?? ''}`.toLowerCase().includes(query.trim().toLowerCase()))
	);

	let menuFor = $state<string | null>(null);
	async function changeRole(m: ProjectMemberRow, select: HTMLSelectElement) {
		// The reload returns the same stored role, so Svelte leaves the DOM alone; put the old value back by hand.
		if (!(await run(`${api}/${m.userId}`, 'PATCH', { role: select.value }))) select.value = m.role;
	}
	async function remove(m: ProjectMemberRow) {
		menuFor = null;
		if (m.userId !== data.me.id) return run(`${api}/${m.userId}`, 'DELETE');
		if (!confirm(`Leave ${project.name}? Another owner will have to add you back.`)) return;
		// Leaving the project: its pages now 404 for you, so go back to the list instead of reloading.
		busy = true;
		error = await send(`${api}/${m.userId}`, 'DELETE');
		busy = false;
		if (!error) await goto('/projects', { invalidateAll: true });
	}
	const reAdd = (m: ProjectMemberRow) => run(api, 'POST', { userId: m.userId, role: m.role });

	// Add-member panel
	let adding = $state(false);
	let search = $state('');
	let candidates = $state<MemberCandidate[]>([]);
	let picked = $state<MemberCandidate | null>(null);
	let newRole = $state<ProjectRole>('Editor');

	$effect(() => {
		const q = search.trim();
		if (!adding || !q) return void (candidates = []);
		const controller = new AbortController();
		const timer = setTimeout(async () => {
			try {
				const res = await fetch(`${api}/candidates?q=${encodeURIComponent(q)}`, { signal: controller.signal });
				if (res.ok) candidates = await res.json();
			} catch {
				// Aborted by a newer keystroke, or offline; the list just stays as it was.
			}
		}, 200);
		return () => (clearTimeout(timer), controller.abort());
	});

	function openPanel() {
		adding = true;
		search = '';
		picked = null;
		newRole = 'Editor';
	}

	async function addPicked() {
		if (picked && (await run(api, 'POST', { userId: picked.userId, role: newRole }))) adding = false;
	}

	const button = 'flex h-9 items-center gap-2 rounded-md px-4 text-[13px] font-semibold disabled:opacity-50';
</script>

<svelte:window onclick={(e) => menuFor && !(e.target as Element).closest('[data-member-menu]') && (menuFor = null)} onkeydown={(e) => e.key === 'Escape' && (menuFor = null)} />

<svelte:head><title>Team · {project.name} · Chaos Lab</title></svelte:head>

{#snippet avatar(m: { userId: string; displayName: string }, muted = false)}
	<span class="grid size-8 shrink-0 place-items-center rounded-full text-[10px] font-semibold {muted ? 'bg-field text-slate-500' : avatarTone(m.userId)}" aria-hidden="true">{initials(m.displayName)}</span>
{/snippet}

{#snippet usersIcon()}
	<svg viewBox="0 0 24 24" class="size-4 text-accent-ink" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><circle cx="9" cy="8" r="3.5" /><path d="M3 20a6 6 0 0 1 12 0M16 4.5a3.5 3.5 0 0 1 0 7M18 14.5a6 6 0 0 1 3 5.5" /></svg>
{/snippet}

<div class="mt-8 flex flex-wrap items-start justify-between gap-4">
	<div>
		<h2 class="text-lg font-semibold">Team</h2>
		<p class="mt-1 text-xs text-slate-400">People with access to {project.name}. Project roles are separate from instance roles like SuperAdmin.</p>
	</div>
	{#if canManage}
		<button type="button" onclick={openPanel} class="{button} bg-accent text-on-accent hover:bg-accent-hover">+ Add member</button>
	{/if}
</div>

{#if error}
	<p role="alert" class="mt-4 rounded-md border border-danger-line bg-danger-panel px-4 py-2 text-[13px] text-rose-300">{error}</p>
{/if}

<div class="mt-6 grid items-start gap-6 {adding ? 'xl:grid-cols-[minmax(0,1fr)_30rem]' : ''}">
	<section aria-labelledby="members-heading" class="rounded-lg border border-line">
		<div class="flex flex-wrap items-center justify-between gap-4 border-b border-line-faint px-5 py-4">
			<div>
				<h3 id="members-heading" class="flex items-center gap-2 font-semibold">{@render usersIcon()} Members</h3>
				<p class="mt-1 text-xs text-slate-400">{team.members.length} active member{team.members.length === 1 ? '' : 's'}</p>
			</div>
			<label class="w-full sm:w-64">
				<span class="sr-only">Search members</span>
				<input bind:value={query} type="search" placeholder="Search members" class="block h-9 w-full rounded-md border border-line bg-panel text-[13px] text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent" />
			</label>
		</div>

		<div class="overflow-x-auto">
			<table class="w-full min-w-[36rem] text-left text-[13px]">
				<thead class="border-b border-line-faint text-[10px] tracking-wider text-slate-500 uppercase">
					<tr><th class="px-5 py-3 font-semibold">Member</th><th class="px-5 py-3 font-semibold">Role</th><th class="px-5 py-3 font-semibold">Added</th><th class="w-12"><span class="sr-only">Actions</span></th></tr>
				</thead>
				<tbody>
					{#each visible as m (m.userId)}
						<tr class="border-b border-line-faint last:border-0 hover:bg-panel">
							<td class="px-5 py-3">
								<div class="flex items-center gap-3">
									{@render avatar(m)}
									<div class="min-w-0"><p class="text-slate-100">{m.displayName}</p><p class="truncate text-[11px] text-slate-500">{m.email ?? ''}</p></div>
								</div>
							</td>
							<td class="px-5 py-3">
								{#if canManage}
									<label>
										<span class="sr-only">Role of {m.displayName}</span>
										<select id="role-{m.userId}" value={m.role} disabled={busy} onchange={(e) => changeRole(m, e.currentTarget)} class="h-8 rounded-md border border-line-strong bg-canvas pr-8 text-[13px] text-slate-100 focus:border-accent focus:ring-1 focus:ring-accent">
											{#each ROLES as r (r.role)}<option value={r.role}>{r.role}</option>{/each}
										</select>
									</label>
								{:else}
									<span class="text-slate-200">{m.role}</span>
								{/if}
							</td>
							<td class="px-5 py-3 text-slate-300">{day(m.joinedAt)}</td>
							<td class="relative px-3 py-3 text-right">
								{#if canManage}
									<div data-member-menu>
										<button type="button" onclick={() => (menuFor = menuFor === m.userId ? null : m.userId)} aria-label="Actions for {m.displayName}" aria-expanded={menuFor === m.userId} class="rounded px-2 text-slate-400 hover:text-slate-100">•••</button>
										{#if menuFor === m.userId}
											<div role="menu" class="absolute top-full right-3 z-10 w-52 rounded-md border border-line bg-panel py-1 text-left shadow-lg">
												<button type="button" role="menuitem" onclick={() => ((menuFor = null), document.getElementById(`role-${m.userId}`)?.focus())} class="block w-full px-4 py-2 text-left text-slate-200 hover:bg-field">Change role…</button>
												<button type="button" role="menuitem" disabled={busy} onclick={() => remove(m)} class="block w-full px-4 py-2 text-left text-rose-300 hover:bg-field">Remove from project</button>
											</div>
										{/if}
									</div>
								{/if}
							</td>
						</tr>
					{:else}
						<tr><td colspan="4" class="px-5 py-8 text-center text-slate-500">No members match “{query}”.</td></tr>
					{/each}
				</tbody>
			</table>
		</div>

		{#if team.removed.length}
			<div class="border-t border-line-faint px-5 py-4">
				<p class="text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Removed ({team.removed.length})</p>
				<ul class="mt-2">
					{#each team.removed as m (m.userId)}
						<li class="flex flex-wrap items-center gap-3 py-2">
							{@render avatar(m, true)}
							<div class="min-w-0 flex-1"><p class="text-slate-400">{m.displayName}</p><p class="truncate text-[11px] text-slate-600">{m.email ?? ''}</p></div>
							<span class="text-[11px] text-slate-500">Removed {day(m.removedAt!)} · was {m.role}</span>
							{#if canManage}
								<button type="button" disabled={busy} onclick={() => reAdd(m)} class="text-[13px] font-semibold text-accent-ink hover:text-accent-ink-strong disabled:opacity-50">↶ Re-add</button>
							{/if}
						</li>
					{/each}
				</ul>
				<p class="mt-1 text-[11px] text-slate-500">Re-adding restores the same membership record, so history stays linked.</p>
			</div>
		{/if}
	</section>

	{#if adding}
		<aside aria-label="Add member" class="rounded-lg border border-line">
			<div class="flex items-start justify-between gap-4 border-b border-line-faint px-5 py-4">
				<div>
					<h3 class="flex items-center gap-2 font-semibold">{@render usersIcon()} Add member</h3>
					<p class="mt-1 text-xs text-slate-400">Only existing accounts on this instance can be added.</p>
				</div>
				<button type="button" onclick={() => (adding = false)} aria-label="Close add member" class="text-slate-400 hover:text-slate-100">✕</button>
			</div>
			<div class="space-y-5 px-5 py-5">
				<label class="block">
					<span class="text-xs text-slate-300">Person</span>
					<!-- svelte-ignore a11y_autofocus -->
					<input bind:value={search} autofocus type="search" placeholder="Name, username or email" class="mt-2 block h-10 w-full rounded-md border border-line bg-panel text-[13px] text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent" />
				</label>

				{#if candidates.length}
					<ul role="listbox" aria-label="Matching accounts" class="space-y-1 rounded-md border border-line p-1">
						{#each candidates as c (c.userId)}
							{@const selected = picked?.userId === c.userId}
							<li role="option" aria-selected={selected} aria-disabled={c.role !== null}>
								<button type="button" disabled={c.role !== null} onclick={() => (picked = c)} class="flex w-full items-center gap-3 rounded px-3 py-2 text-left {selected ? 'bg-accent-tint' : 'enabled:hover:bg-field'} disabled:opacity-50">
									{@render avatar(c, c.role !== null)}
									<span class="min-w-0 flex-1">
										<span class="block text-[13px] text-slate-100">{c.displayName}</span>
										<span class="block truncate text-[11px] text-slate-500">{c.role ? 'Already a member' : (c.email ?? '')}</span>
									</span>
									{#if c.role}<span class="text-[11px] text-slate-500">{c.role}</span>{:else if selected}<span class="text-accent-ink" aria-hidden="true">✓</span>{/if}
								</button>
							</li>
						{/each}
					</ul>
				{:else if search.trim()}
					<p class="text-xs text-slate-500">No matching accounts.</p>
				{/if}

				<fieldset>
					<legend class="text-xs text-slate-300">Project role</legend>
					<div class="mt-2 inline-flex rounded-md border border-line p-1">
						{#each ROLES as r (r.role)}
							<label class="cursor-pointer rounded px-4 py-1.5 text-[13px] {newRole === r.role ? 'bg-field font-semibold text-slate-100' : 'text-slate-400'} has-focus-visible:ring-1 has-focus-visible:ring-accent">
								<input type="radio" bind:group={newRole} value={r.role} class="sr-only" />{r.role}
							</label>
						{/each}
					</div>
				</fieldset>

				<dl class="space-y-3 rounded-md border border-line p-4 text-xs">
					{#each ROLES as r (r.role)}
						<div class="grid grid-cols-[4rem_1fr] gap-3">
							<dt class="font-semibold {newRole === r.role ? 'text-accent-ink' : 'text-slate-100'}">{r.role}</dt>
							<dd class="text-slate-400">{r.text}</dd>
						</div>
					{/each}
				</dl>

				<div class="flex justify-end gap-3">
					<button type="button" onclick={() => (adding = false)} class="{button} border border-line-strong text-slate-200 hover:bg-panel">Cancel</button>
					<button type="button" disabled={!picked || busy} onclick={addPicked} class="{button} bg-accent text-on-accent hover:bg-accent-hover">
						+ {picked ? `Add ${picked.displayName}` : 'Add member'}
					</button>
				</div>
			</div>
		</aside>
	{/if}
</div>
