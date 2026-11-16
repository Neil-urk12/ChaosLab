import { useColorScheme } from 'react-native';

// Same slate neutrals and emerald/amber/rose status colors as the web app.
export const Colors = {
  light: {
    text: '#0f172a',
    dim: '#55627a',
    background: '#f4f6f9',
    card: '#ffffff',
    border: '#d5dbe5',
    accent: '#047857',
    good: '#047857',
    warn: '#a65a07',
    bad: '#be123c',
  },
  dark: {
    text: '#e6ebf4',
    dim: '#9aa7bd',
    background: '#0b1220',
    card: '#121b2e',
    border: '#2a3752',
    accent: '#34d399',
    good: '#34d399',
    warn: '#fbbf24',
    bad: '#fb7185',
  },
} as const;

export type Theme = (typeof Colors)['light' | 'dark'];

export const useTheme = (): Theme => Colors[useColorScheme() === 'dark' ? 'dark' : 'light'];
