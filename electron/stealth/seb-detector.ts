// Detects if the application is running inside Safe Exam Browser

import { exec } from 'child_process';
import { promisify } from 'util';

const execAsync = promisify(exec);

export class SEBDetector {
    private isSEBDetected: boolean = false;
    private checkInterval: NodeJS.Timeout | null = null;

    async detectSEB(): Promise<boolean> {
        try {
            const { stdout } = await execAsync('tasklist /FI "IMAGENAME eq SafeExamBrowser.exe" /NH');
            if (stdout.toLowerCase().includes('safeexambrowser.exe')) {
                this.isSEBDetected = true;
                console.log('Safe Exam Browser detected - enabling stealth mode');
                return true;
            }
            const { stdout: stdout2 } = await execAsync('tasklist /FI "IMAGENAME eq SEBClient.exe" /NH');
            if (stdout2.toLowerCase().includes('sebclient.exe')) {
                this.isSEBDetected = true;
                console.log('Safe Exam Browser Client detected - enabling stealth mode');
                return true;
            }
            this.isSEBDetected = false;
            return false;
        } catch (error) {
            console.error('Error detecting SEB:', error);
            return false;
        }
    }

    startMonitoring(callback: (detected: boolean) => void, intervalMs: number = 5000): void {
        this.stopMonitoring();
        this.checkInterval = setInterval(async () => {
            const detected = await this.detectSEB();
            callback(detected);
        }, intervalMs);
        this.detectSEB().then(callback);
    }

    stopMonitoring(): void {
        if (this.checkInterval) {
            clearInterval(this.checkInterval);
            this.checkInterval = null;
        }
    }

    isSEBRunning(): boolean {
        return this.isSEBDetected;
    }

    async isKioskMode(): Promise<boolean> {
        try {
            const { stdout } = await execAsync('tasklist /V /FI "IMAGENAME eq SafeExamBrowser.exe"');
            return stdout.toLowerCase().includes('running');
        } catch (error) {
            return false;
        }
    }
}

export const sebDetector = new SEBDetector();
export default sebDetector;
