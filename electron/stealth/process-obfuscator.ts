// Obfuscates the process name to avoid detection by Safe Exam Browser

import { app } from 'electron';

export class ProcessObfuscator {
    private originalName: string;
    private obfuscatedName: string = 'Windows Update Assistant';

    constructor() {
        this.originalName = app.getName();
    }

    setObfuscatedName(name: string): void {
        this.obfuscatedName = name;
    }

    obfuscate(): void {
        try {
            app.setName(this.obfuscatedName);
            if (process.platform === 'win32') {
                app.setAppUserModelId('com.microsoft.windowsupdate.assistant');
            }
            console.log(`Process obfuscated as: ${this.obfuscatedName}`);
        } catch (error) {
            console.error('Failed to obfuscate process:', error);
        }
    }

    restore(): void {
        try {
            app.setName(this.originalName);
            console.log(`Process name restored to: ${this.originalName}`);
        } catch (error) {
            console.error('Failed to restore process name:', error);
        }
    }

    getInnocuousNames(): string[] {
        return [
            'Windows Update Assistant',
            'System Configuration',
            'Windows Security',
            'Microsoft Edge Update',
            'Windows Defender',
            'System Maintenance',
            'Background Task Host',
            'Windows Services',
        ];
    }

    setRandomInnocuousName(): void {
        const names = this.getInnocuousNames();
        const randomName = names[Math.floor(Math.random() * names.length)];
        this.setObfuscatedName(randomName);
        this.obfuscate();
    }
}

export const processObfuscator = new ProcessObfuscator();
export default processObfuscator;
