<script lang="ts">
	import { goto } from "$app/navigation";
	import PasswordField from "$lib/components/PasswordField.svelte";

	let identifier = $state("");
	let password = $state("");
	let error = $state("");
	let submitting = $state(false);

	async function onsubmit(event: SubmitEvent) {
		event.preventDefault();
		error = "";
		submitting = true;
		try {
			const res = await fetch("/api/auth/login", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({ identifier, password }),
			});
			if (res.status === 401) {
				error = "Invalid username, email, or password.";
				return;
			}
			if (!res.ok) throw new Error(`Login failed with ${res.status}`);

			const me = await fetch("/api/auth/me");
			if (!me.ok)
				throw new Error(`Loading the profile failed with ${me.status}`);
			const { mustChangePassword } = await me.json();
			await goto(mustChangePassword ? "/setup" : "/");
		} catch {
			error = "Sign-in failed. Check that the server is running and try again.";
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head><title>Sign in · ChaosAPI Lab</title></svelte:head>

<h1 class="text-center text-xl font-bold tracking-tight">Welcome back</h1>
<p class="mt-3 text-center text-xs text-slate-400">
	Sign in to your ChaosAPI Lab instance.
</p>

<form {onsubmit} class="mt-10 space-y-6">
	<div>
		<label
			for="identifier"
			class="mb-2 block text-xs font-medium text-slate-200"
			>Username or email</label
		>
		<input
			id="identifier"
			type="text"
			placeholder="you@example.com"
			autocomplete="username"
			required
			bind:value={identifier}
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

	{#if error}
		<p class="text-xs text-rose-400" role="alert">{error}</p>
	{/if}

	<button type="submit" class="auth-submit mt-10" disabled={submitting}>
		{submitting ? "Signing in…" : "Sign in"}
		<svg
			viewBox="0 0 24 24"
			class="size-4"
			fill="none"
			stroke="currentColor"
			stroke-width="2"
			aria-hidden="true"
		>
			<path d="M5 12h14M13 6l6 6-6 6" />
		</svg>
	</button>
</form>

<p
	class="mt-6 border-t border-[#1a1f2b] pt-5 text-center text-[11px] text-slate-500"
>
	Need access? Contact your instance administrator.
</p>
