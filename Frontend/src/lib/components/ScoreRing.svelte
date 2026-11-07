<script lang="ts">
	import { score } from '$lib/format';

	let { value, size = 120 }: { value: number | null; size?: number } = $props();

	const circumference = 2 * Math.PI * 42;
	const filled = $derived(((value ?? 0) / 100) * circumference);
</script>

<div class="relative grid shrink-0 place-items-center" style:width="{size}px" style:height="{size}px">
	<svg viewBox="0 0 100 100" class="absolute inset-0 -rotate-90" aria-hidden="true">
		<circle cx="50" cy="50" r="42" fill="none" stroke="#1e2433" stroke-width="7" />
		<circle
			cx="50"
			cy="50"
			r="42"
			fill="none"
			stroke="#7b7dfb"
			stroke-width="7"
			stroke-linecap="round"
			stroke-dasharray="{filled} {circumference}"
		/>
	</svg>
	<p class="text-center leading-none">
		<span class="block font-bold" style:font-size="{size * 0.27}px">{score(value)}</span>
		<span class="mt-1 block text-[11px] text-slate-500">/ 100</span>
	</p>
</div>
