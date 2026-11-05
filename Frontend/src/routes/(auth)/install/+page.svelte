<script lang="ts">
	import { goto } from "$app/navigation";
	import { onMount } from "svelte";
	import PasswordField from "$lib/components/PasswordField.svelte";

	let token = $state("");
	let ready = $state(false);
	let username = $state("");
	let email = $state("");
	let firstName = $state("");
	let lastName = $state("");
	let password = $state("");
	let confirm = $state("");
	let submitting = $state(false);
	let blocked = $state(false);
	let errors = $state<string[]>([]);

	onMount(() => {
		token = new URLSearchParams(window.location.hash.slice(1)).get("token") ?? "";
		ready = true;
		if (!/^[A-Fa-f0-9]{64}$/.test(token)) {
			blocked = true;
			errors = ["Open the complete setup link printed in the backend terminal."];
		}
	});

	async function onsubmit(event: SubmitEvent) {
		event.preventDefault();
		if (submitting || blocked || !ready) return;
		errors = [];
		if (password !== confirm) {
			errors = ["Passwords don't match. Re-enter your password."];
			return;
		}
		submitting = true;
		try {
			const res = await fetch("/api/auth/setup", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({ token, username, email, firstName, lastName, password }),
			});
			if (res.status === 400) {
				const problem = await res.json();
				errors = Object.values(problem.errors ?? {}).flat() as string[];
				if (!errors.length) errors = ["Check the account details and try again."];
				return;
			}
			if (res.status === 403 || res.status === 409) {
				blocked = true;
				errors = [res.status === 409
					? "This instance is already set up. Sign in with your existing account."
					: "This setup link is invalid or expired. Restart the uninitialized backend and use its new link."];
				return;
			}
			if (!res.ok) throw new Error(`Setup failed with ${res.status}`);
			// Replace the setup history entry so Back does not return to the secret link.
			await goto("/", { replaceState: true });
		} catch {
			errors = ["Setup could not be confirmed. Try signing in if your account was created, or retry when the server is available."];
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Set up instance · ChaosAPI Lab</title>
	<meta name="referrer" content="no-referrer" />
</svelte:head>

<h1 class="text-center text-xl font-bold tracking-tight">Set up your ChaosAPI Lab</h1>
<p class="mt-3 text-center text-sm text-slate-400">
	Create the first SuperAdmin account for this instance.
</p>

{#if errors.length}
	<ul class="mt-6 space-y-1 text-sm text-rose-400" role="alert">
		{#each errors as message}
			<li>{message}</li>
		{/each}
	</ul>
{/if}

{#if ready && !blocked}
	<form {onsubmit} class="mt-8 space-y-5">
		<div class="grid grid-cols-1 gap-5 sm:grid-cols-2">
			<div>
				<label for="first-name" class="mb-2 block text-xs font-medium text-slate-200">First name</label>
				<input id="first-name" autocomplete="given-name" required maxlength="100" bind:value={firstName} class="auth-input" />
			</div>
			<div>
				<label for="last-name" class="mb-2 block text-xs font-medium text-slate-200">Last name</label>
				<input id="last-name" autocomplete="family-name" required maxlength="100" bind:value={lastName} class="auth-input" />
			</div>
		</div>
		<div>
			<label for="username" class="mb-2 block text-xs font-medium text-slate-200">Username</label>
			<input id="username" autocomplete="username" required maxlength="256" bind:value={username} class="auth-input" />
		</div>
		<div>
			<label for="email" class="mb-2 block text-xs font-medium text-slate-200">Email</label>
			<input id="email" type="email" autocomplete="email" required maxlength="256" bind:value={email} class="auth-input" />
		</div>
		<PasswordField id="password" label="Password" placeholder="Choose a strong password" autocomplete="new-password" bind:value={password} />
		<p class="text-xs text-slate-400">Use at least 6 characters, including uppercase, lowercase, a number, and a symbol.</p>
		<PasswordField id="confirm-password" label="Confirm password" placeholder="Re-enter your password" autocomplete="new-password" bind:value={confirm} />
		<button type="submit" class="auth-submit" disabled={submitting}>
			{submitting ? "Creating account…" : "Create SuperAdmin & sign in"}
		</button>
	</form>
{/if}

<p class="mt-6 text-center text-xs text-slate-400">
	Already have an account? <a href="/login" class="text-slate-200 underline underline-offset-4">Sign in</a>
</p>
