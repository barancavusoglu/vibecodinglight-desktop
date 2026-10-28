# Manual hardware validation

These are manual release checks for an existing VibeCodingLight robot with compatible firmware. Source builds and simulated/service tests do not establish physical-device support. Do not flash firmware, modify wiring or change a user's assistant trust configuration as part of these checks.

1. Confirm the app/platform/firmware versions. Current desktop light control requires firmware 0.3.0, protocol v1 and the `FX1` capability. Firmware source and physical designs are maintained separately from this repository.
2. With a known USB-C **data** cable, connect the robot to the computer. Confirm the app's actual connection card reports USB and run its brief color test. A charge-only cable cannot establish USB data.
3. Generate a local working session, a real user-input wait, an idle session and a mixed working/idle case. Compare the app and physical light. Red takes priority; do not treat automatic permission review as a sustained user wait.
4. Disconnect the USB host data connection and power the robot from a suitable power bank using a USB-C charging cable. Enable Bluetooth on the computer and follow OS permission prompts. Confirm a verified Bluetooth connection and repeat the color/session checks.
5. Reconnect a USB host data connection and verify USB takes priority. Power-bank power alone does not count as a USB host connection.
6. Check unplug/reconnect, application quit/restart, brightness and idle timeout. On loss of desktop communication, the firmware watchdog should stop the light within its documented six-second window.

Record actual observations with OS/architecture, connection mode and versions. Do not assume success from a port scan, successful build or another platform's result. Mac additionally needs real CoreBluetooth/privacy-prompt and Swift-helper checks. See [integration scope](INTEGRATIONS.md) and [release preparation](RELEASING.md).
