import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router';
import { useColorScheme } from 'react-native';

import { useTheme } from '@/constants/theme';
import { baseUrl } from '@/lib/api';
import { SessionProvider, useSession } from '@/lib/session';
import { Notice } from '@/lib/ui';

export default function RootLayout() {
  const theme = useTheme();
  const base = useColorScheme() === 'dark' ? DarkTheme : DefaultTheme;
  return (
    <ThemeProvider
      value={{
        ...base,
        colors: { ...base.colors, background: theme.background, card: theme.card, text: theme.text, border: theme.border, primary: theme.accent },
      }}>
      <SessionProvider>
        <Screens />
      </SessionProvider>
    </ThemeProvider>
  );
}

function Screens() {
  const { state, retry, signOut } = useSession();

  if (!baseUrl)
    return <Notice title="No server address" text="This build was made without EXPO_PUBLIC_API_URL, so it does not know where ChaosLab is. Set it and rebuild." />;
  if (state.status === 'loading') return <Notice />;
  if (state.status === 'unreachable')
    return <Notice title="Can't reach ChaosLab" text={`No answer from ${baseUrl}. Check your connection and that the server is running.`} action={['Try again', retry]} />;
  // Every data endpoint answers 403 until the password is changed, and this viewer cannot change it.
  if (state.status === 'signedIn' && state.user.mustChangePassword)
    return (
      <Notice
        title="Password change required"
        text="Sign in to the ChaosLab web app to set a new password, then sign in here again."
        action={['Sign out', signOut]}
      />
    );

  const signedIn = state.status === 'signedIn';
  return (
    <Stack screenOptions={{ headerBackButtonDisplayMode: 'minimal' }}>
      <Stack.Protected guard={signedIn}>
        <Stack.Screen name="index" options={{ title: 'Projects' }} />
        <Stack.Screen name="projects/[id]/index" options={{ title: 'Project' }} />
        <Stack.Screen name="projects/[id]/runs/[runId]/index" options={{ title: 'Run' }} />
        <Stack.Screen name="projects/[id]/runs/[runId]/scenarios/[executionId]" options={{ title: 'Scenario' }} />
        <Stack.Screen name="projects/[id]/runs/[runId]/findings/[findingId]" options={{ title: 'Finding' }} />
      </Stack.Protected>
      <Stack.Protected guard={!signedIn}>
        <Stack.Screen name="login" options={{ headerShown: false }} />
      </Stack.Protected>
    </Stack>
  );
}
