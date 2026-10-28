namespace AiLight.Core;

public static class SettingSteps {
    public static readonly int[] IdleMinutes = [1, 5, 10, 15, 30, 60, 120, 240];
    public static readonly int[] BrightnessLevels = [15, 30, 50, 70, 85, 100];
    public static int BrightnessLevel(int percent) => Enumerable.Range(0, BrightnessLevels.Length).MinBy(i => Math.Abs((long)BrightnessLevels[i] - percent));
    public static int BrightnessPercent(int level) => BrightnessLevels[Math.Clamp(level, 0, BrightnessLevels.Length - 1)];
    public static int NearestIdleMinutes(int minutes) => IdleMinutes.MinBy(value => Math.Abs((long)value - minutes));
}
