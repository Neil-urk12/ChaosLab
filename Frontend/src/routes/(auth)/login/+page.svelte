<script lang="ts">
	import PasswordField from '$lib/components/PasswordField.svelte';

	let email = $state('');
	let password = $state('');

	function onsubmit(event: SubmitEvent) {
		event.preventDefault();
		// shortcut: not wired to the backend yet, replace with the auth API call once it exists
	}
</script>

<svelte:head><title>Sign in · ChaosAPI Lab</title></svelte:head>

<h1 class="text-center text-xl font-bold tracking-tight">Welcome back</h1>
<p class="mt-3 text-center text-xs text-slate-400">Sign in to your ChaosAPI Lab instance.</p>

<form {onsubmit} class="mt-10 space-y-6">
	<div>
		<label for="email" class="mb-2 block text-xs font-medium text-slate-200">Email</label>
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
		id="password"
		label="Password"
		placeholder="Enter your password"
		autocomplete="current-password"
		bind:value={password}
	/>

	<button type="submit" class="auth-submit mt-10">
		Sign in
		<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
			<path d="M5 12h14M13 6l6 6-6 6" />
		</svg>
	</button>
</form>

<p class="mt-6 border-t border-[#1a1f2b] pt-5 text-center text-[11px] text-slate-500">
	Need access? Contact your instance administrator.
</p>
