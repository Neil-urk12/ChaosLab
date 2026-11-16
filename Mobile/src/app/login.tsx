import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, TextInput, View } from 'react-native';

import { useTheme } from '@/constants/theme';
import { useSession } from '@/lib/session';
import { Button, Txt, inputStyle } from '@/lib/ui';

export default function Login() {
  const theme = useTheme();
  const { signIn } = useSession();
  const [identifier, setIdentifier] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    if (busy || !identifier.trim() || !password) return;
    setBusy(true);
    const problem = await signIn(identifier, password);
    // On success the layout replaces this screen, so there is nothing left to update.
    if (problem) {
      setError(problem);
      setBusy(false);
    }
  }

  return (
    <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={[styles.screen, { backgroundColor: theme.background }]}>
      <View style={styles.form}>
        <Txt variant="title" accessibilityRole="header">
          ChaosLab
        </Txt>
        <Txt tone="dim">Sign in to view your projects and test results.</Txt>
        <TextInput
          accessibilityLabel="Username or email"
          placeholder="Username or email"
          placeholderTextColor={theme.dim}
          autoCapitalize="none"
          autoCorrect={false}
          autoComplete="username"
          textContentType="username"
          returnKeyType="next"
          value={identifier}
          onChangeText={setIdentifier}
          style={inputStyle(theme)}
        />
        <TextInput
          accessibilityLabel="Password"
          placeholder="Password"
          placeholderTextColor={theme.dim}
          secureTextEntry
          autoComplete="current-password"
          textContentType="password"
          returnKeyType="go"
          value={password}
          onChangeText={setPassword}
          onSubmitEditing={submit}
          style={inputStyle(theme)}
        />
        {error ? (
          <Txt variant="small" tone="bad" accessibilityRole="alert">
            {error}
          </Txt>
        ) : null}
        <Button label={busy ? 'Signing in…' : 'Sign in'} onPress={submit} disabled={busy || !identifier.trim() || !password} />
      </View>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, justifyContent: 'center' },
  form: { padding: 24, gap: 12 },
});
