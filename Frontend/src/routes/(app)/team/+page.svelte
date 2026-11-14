<script lang="ts">
	import PasswordField from '$lib/components/PasswordField.svelte';
	import { send } from '$lib/problem';

	let { data } = $props();

	const isSuperAdmin = $derived(data.me.roles.includes('SuperAdmin'));
	const blank = { firstName: '', lastName: '', username: '', email: '', password: '' };
	let form = $state({ ...blank });
	let submitting = $state(false);
	let error = $state<string | null>(null);
	let created = $state<string | null>(null);

	const inputClass =
		'block h-10 w-full rounded-md border border-line bg-panel px-3 text-[13px] text-slate-100 placeholder:text-slate-500 focus:border-accent focus:ring-1 focus:ring-accent';
	const labelClass = 'mb-2 block text-xs font-medium text-slate-200';

	async function onsubmit(event: SubmitEvent) {
		event.preventDefault();
		if (submitting) return;
		submitting = true;
		error = created = null;
		error = await send('/api/users', 'POST', form);
		if (!error) {
			created = form.username.trim();
			form = { ...blank };
		}
		submitting = false;
	}
</script>

<svelte:head>
	<title>Team · ChaosAPI Lab</title>
</svelte:head>

<p class="text-[10px] font-semibold tracking-wide text-slate-400 uppercase">Manage / Team</p>
<h1 class="mt-2 text-3xl font-bold tracking-tight">Team</h1>

{#if !isSuperAdmin}
	<p class="mt-4 text-sm text-slate-400">
		Only the SuperAdmin can create accounts. To add someone to a project, open the project's Team tab.
	</p>
{:else}
	<p class="mt-2 text-sm text-slate-400">
		Create an account with a temporary password. The person must change it the first time they sign in, then a
		project owner can add them from the project's Team tab.
	</p>

	<form {onsubmit} class="mt-8 max-w-xl space-y-5 rounded-lg border border-line p-6">
		<h2 class="text-[15px] font-semibold">New account</h2>
		<div class="grid grid-cols-1 gap-5 sm:grid-cols-2">
			<div>
				<label for="first-name" class={labelClass}>First name</label>
				<input id="first-name" required maxlength="100" autocomplete="off" bind:value={form.firstName} class={inputClass} />
			</div>
			<div>
				<label for="last-name" class={labelClass}>Last name</label>
				<input id="last-name" required maxlength="100" autocomplete="off" bind:value={form.lastName} class={inputClass} />
			</div>
		</div>
		<div>
			<label for="username" class={labelClass}>Username</label>
			<input id="username" required maxlength="256" autocomplete="off" bind:value={form.username} class={inputClass} />
		</div>
		<div>
			<label for="email" class={labelClass}>Email</label>
			<input id="email" type="email" required maxlength="256" autocomplete="off" bind:value={form.email} class={inputClass} />
		</div>
		<PasswordField
			id="temporary-password"
			label="Temporary password"
			placeholder="Share this with the person securely"
			autocomplete="new-password"
			bind:value={form.password}
		/>
		<p class="text-xs text-slate-400">Use at least 6 characters, including uppercase, lowercase, a number, and a symbol.</p>

		{#if error}<p class="text-sm text-rose-400" role="alert">{error}</p>{/if}
		{#if created}
			<p class="text-sm text-emerald-400" role="status">
				Created {created}. They'll be asked to choose a new password when they sign in.
			</p>
		{/if}

		<button
			type="submit"
			disabled={submitting}
			class="flex h-9 items-center rounded-md bg-accent px-4 text-[13px] font-semibold text-on-accent hover:bg-accent-hover disabled:opacity-60"
		>
			{submitting ? 'Creating account…' : 'Create account'}
		</button>
	</form>
{/if}
