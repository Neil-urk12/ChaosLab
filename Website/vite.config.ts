import tailwindcss from '@tailwindcss/vite';
import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig, loadEnv } from 'vite';

export default defineConfig(({ mode }) => {
	// GitHub Pages serves the site under /ChaosLab, so the deploy workflow sets BASE_PATH; local dev runs at /.
	const base = (loadEnv(mode, '.', '').BASE_PATH ?? '') as '' | `/${string}`;

	return {
		plugins: [
			tailwindcss(),
			sveltekit({
				compilerOptions: {
					// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
					runes: ({ filename }) =>
						filename.split(/[/\\]/).includes('node_modules') ? undefined : true
				},
				// Every page is prerendered; 404.html lets GitHub Pages show SvelteKit's error page for unknown paths.
				adapter: adapter({ fallback: '404.html' }),
				paths: { base }
			})
		]
	};
});
