// Message for a failed API response: the first validation error of a 400, the text body of a 409/422,
// or a generic message.
export async function problemMessage(res: Response): Promise<string> {
	if (res.status === 400) {
		const problem = await res.json().catch(() => null);
		return Object.values<string[]>(problem?.errors ?? {})[0]?.[0] ?? 'Check the values and try again.';
	}
	if (res.status === 409 || res.status === 422) return (await res.json().catch(() => null)) ?? 'The request could not be completed.';
	if (res.status === 403) return "You don't have permission to do this.";
	return 'The request failed. Try again.';
}

// Sends a JSON request; returns null on success or a message to show.
export async function send(url: string, method: string, body?: unknown): Promise<string | null> {
	try {
		const res = await fetch(url, {
			method,
			headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
			body: body === undefined ? undefined : JSON.stringify(body)
		});
		return res.ok ? null : await problemMessage(res);
	} catch {
		return 'Could not reach the server.';
	}
}
