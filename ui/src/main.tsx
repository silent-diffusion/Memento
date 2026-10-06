import { effect } from '@preact/signals';
import { render } from 'preact';
import './styles/fonts.css';
import './styles/tokens.css';
import './styles/base.css';
import './styles/shell.css';
import './styles/library-empty.css';
import { App } from './App';
import { createBridgeClient } from './bridge/client';
import { installPressFeedback } from './pressFeedback';
import { startUp } from './state/startup';
import { connectEvents, createStore } from './state/store';
import { applyTheme, systemPrefersDark } from './theme';

const root = document.getElementById('root');
if (root === null) {
  throw new Error('index.html is missing the #root element.');
}

// The host keeps WebView2's colour-scheme preference in step with Windows and Settings,
// so the first paint already has the right theme; app.version and theme.changed confirm it.
const store = createStore(systemPrefersDark());
const bridge = createBridgeClient();
connectEvents(bridge, store);
effect(() => {
  applyTheme(document.documentElement, store.isDark.value);
});
installPressFeedback(document);

render(<App bridge={bridge} store={store} />, root);

startUp(bridge, store).catch((error: unknown) => {
  console.warn('[startup] ui.ready could not be sent', error);
});
