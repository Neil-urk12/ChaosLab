<script lang="ts">
	import PasswordField from '$lib/components/PasswordField.svelte';

	let email = $state('');
	let password = $state('');
	let confirm = $state('');
	let submitted = $state(false);

	let mismatch = $derived(submitted && confirm !== password);

	function onsubmit(event: SubmitEvent) {
		event.preventDefault();
		submitted = true;
		if (confirm !== password) return;
		// shortcut: not wired to the backend yet, replace with the bootstrap API call once it exists
	}
</script>

<svelte:head><title>Set up administrator · ChaosAPI Lab</title></svelte:head>

<h1 class="text-center text-xl font-bold tracking-tight">Secure your ChaosAPI Lab</h1>
<p class="mt-3 text-center text-xs text-slate-400">Set the credentials for your administrator account.</p>

<form {onsubmit} class="mt-10 space-y-5">
	<div>
		<label for="email" class="mb-2 block text-xs font-medium text-slate-200">Administrator email</label>
		<input
			id="email"
			type="email"
			placeholder="you@example.com"
			autocomplete="email"
			required
			bind:value={email}
			class="auth-input"
		/>
	</div>

	<PasswordField
		id="new-password"
		label="New password"
		placeholder="Enter a new password"
		autocomplete="new-password"
		bind:value={password}
	/>

	<div>
		<PasswordField
			id="confirm-password"
			label="Confirm password"
			placeholder="Re-enter your password"
			autocomplete="new-password"
			bind:value={confirm}
		/>
		{#if mismatch}
			<p class="mt-2 text-xs text-rose-400" role="alert">Passwords don't match. Re-enter the new password.</p>
		{/if}
	</div>

	<button type="submit" class="auth-submit mt-8">
		Save credentials & sign in
		<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
			<path d="M5 12h14M13 6l6 6-6 6" />
		</svg>
	</button>
</form>

<div class="mt-6 space-y-2 border-t border-[#1a1f2b] pt-5 text-center text-[11px] text-slate-500">
	<p class="text-slate-400">Replaces the default administrator credentials.</p>
	<p>This step appears only when setting up the instance.</p>
</div>
