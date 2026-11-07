<script lang="ts">
	let { children } = $props();
</script>

<div
	class="relative flex min-h-screen flex-col items-center justify-center bg-canvas px-4 py-16 font-['Inter_Variable',system-ui,sans-serif] text-slate-100"
>
	<main class="w-full max-w-[22rem]">
		{@render children()}
	</main>

	<p class="absolute bottom-5 left-5 flex items-center gap-2 text-[11px] text-slate-500">
		<span class="size-1.5 rounded-full bg-accent" aria-hidden="true"></span>
		Self-hosted · Community edition
	</p>
</div>
