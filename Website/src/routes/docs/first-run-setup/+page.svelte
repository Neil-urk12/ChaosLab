<script lang="ts">
	import { resolve } from '$app/paths';
</script>

<h1>First-run setup</h1>

<p>
	A new instance has no accounts and no default credentials. First-run setup creates its
	<strong>SuperAdmin</strong>, the account that administers the instance and creates everyone
	else's account. It happens once per instance.
</p>

<h2>Find the setup link</h2>
<p>
	On an instance that has not been set up, the backend prints a setup link when it starts. When you
	install from source, it is in the <code>make start</code> terminal and looks like this:
</p>
<pre><code>http://localhost:5173/install#token=…</code></pre>
<p>
	The pod's backend container prints a link too (<code>make logs-backend</code>), but with the
	from-source setup use the one from <code>make start</code>: the web app on <code>:5173</code> talks
	to that backend.
</p>

<h2>Create the SuperAdmin</h2>
<ol>
	<li>Open the complete link, including everything after <code>#</code>, within one hour.</li>
	<li>Enter your name, username, email and password.</li>
	<li>ChaosLab creates the SuperAdmin account and signs you in.</li>
</ol>
<p>
	After that, the instance shows the sign-in page. There is no public registration: the SuperAdmin
	creates other users' accounts, and those users change their password at first sign-in.
</p>

<h2>If the link expired</h2>
<p>
	Restart the backend to get a new link. The old link stops working. Once setup is done, no link is
	printed again, and deleting or disabling the SuperAdmin does not reopen setup.
</p>

<h2>Keep the link secret</h2>
<p>
	Anyone who opens the link first becomes the SuperAdmin. Treat the terminal output and container
	logs that contain it as secrets until setup is done.
</p>

<h2>Another address</h2>
<p>
	The link points to <code>http://localhost:5173</code>. If people reach the instance at another
	address, set <code>BootstrapSetup__PublicUrl</code> (see
	<a href={resolve('/docs/configuration')}>Configuration</a>).
</p>
