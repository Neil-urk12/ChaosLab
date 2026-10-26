<script lang="ts">
	let { children } = $props();
</script>

<svelte:head>
	<link rel="preconnect" href="https://fonts.googleapis.com" />
	<link
		rel="stylesheet"
		href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"
	/>
</svelte:head>

<div
	class="relative flex min-h-screen flex-col items-center justify-center bg-[#07090e] px-4 py-16 font-[Inter,system-ui,sans-serif] text-slate-100"
>
	<main class="w-full max-w-[22rem]">
		{@render children()}
	</main>

	<p class="absolute bottom-5 left-5 flex items-center gap-2 text-[11px] text-slate-500">
		<span class="size-1.5 rounded-full bg-[#7b7dfb]" aria-hidden="true"></span>
		Self-hosted · Community edition
	</p>
</div>
