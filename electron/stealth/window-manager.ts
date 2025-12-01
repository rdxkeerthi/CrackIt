// Manages stealth window properties to avoid detection

import { BrowserWindow } from 'electron';

export class StealthWindowManager {
    makeWindowStealth(window: BrowserWindow): void {
        window.setSkipTaskbar(true);
        if (process.platform === 'win32') {
            window.setSkipTaskbar(true);
        }
        window.setTitle('');
        console.log('Stealth properties applied to window');
    }

    makeWindowTransparent(window: BrowserWindow, clickThrough: boolean = false): void {
        window.setOpacity(0.3);
        if (clickThrough) {
            window.setIgnoreMouseEvents(true, { forward: true });
        }
    }

    restoreWindow(window: BrowserWindow, title: string): void {
        window.setSkipTaskbar(false);
        window.setTitle(title);
        window.setOpacity(1.0);
        window.setIgnoreMouseEvents(false);
        console.log('Window properties restored');
    }

    isStealthMode(window: BrowserWindow): boolean {
        // Check if window is set to skip taskbar (stealth mode indicator)
        // Since isSkipTaskbar doesn't exist as a getter, we assume stealth if title is empty
        return window.getTitle() === '';
    }

    setSubtleAlwaysOnTop(window: BrowserWindow): void {
        window.setAlwaysOnTop(true, 'normal');
        window.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
    }

    hideFromScreenCapture(window: BrowserWindow): void {
        try {
            window.setContentProtection(true);
        } catch (error) {
            console.warn('Could not enable content protection:', error);
        }
    }
}

export const stealthWindowManager = new StealthWindowManager();
export default stealthWindowManager;
