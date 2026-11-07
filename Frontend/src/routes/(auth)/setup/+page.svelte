<script lang="ts">
	import PasswordField from "$lib/components/PasswordField.svelte";
	import { goto } from "$app/navigation";

	let currentPassword = $state("");
	let password = $state("");
	let confirm = $state("");
	let submitted = $state(false);
	let isSubmitting = $state(false);
	let errors = $state<string[]>([]);

	let mismatch = $derived(submitted && confirm !== password);

	async function onsubmit(event: SubmitEvent) {
		event.preventDefault();
		submitted = true;
		errors = [];
		if (confirm !== password) return;
		isSubmitting = true;

		try {
			const res = await fetch("/api/auth/change-password", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({ currentPassword, newPassword: password }),
			});
			if (res.status === 401 || res.status === 403) {
				await goto("/login");
				return;
			}
			if (res.status === 400) {
				const problem = await res.json();
				errors = Object.values(problem.errors ?? {}).flat() as string[];
				return;
			}
			if (!res.ok) throw new Error(`Password change failed with ${res.status}`);
			await goto("/");
		} catch {
			errors = ["Saving failed. Check that the server is running and try again."];
		} finally {
			isSubmitting = false;
		}
	}
</script>

<svelte:head><title>Change password · ChaosAPI Lab</title></svelte:head>

<h1 class="text-center text-xl font-bold tracking-tight">
	Secure your ChaosAPI Lab
</h1>
<p class="mt-3 text-center text-xs text-slate-400">
	Choose a new password before continuing to your account.
</p>

<form {onsubmit} class="mt-10 space-y-5">
	<PasswordField
		id="current-password"
		label="Current password"
		placeholder="Enter your current password"
		autocomplete="current-password"
		bind:value={currentPassword}
	/>

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
			<p class="mt-2 text-xs text-rose-400" role="alert">
				Passwords don't match. Re-enter the new password.
			</p>
		{/if}
	</div>

	{#if errors.length}
		<ul class="space-y-1 text-xs text-rose-400" role="alert">
			{#each errors as message}
				<li>{message}</li>
			{/each}
		</ul>
	{/if}

	<button type="submit" class="auth-submit mt-8" disabled={isSubmitting}>
		{isSubmitting ? "Saving…" : "Save password & continue"}
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

<div
	class="mt-6 space-y-2 border-t border-line-faint pt-5 text-center text-[11px] text-slate-500"
>
	<p class="text-slate-400">Your administrator requires a password change.</p>
</div>
