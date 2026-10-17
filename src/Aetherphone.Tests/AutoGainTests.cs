using Aetherphone.Core.Telephony.Audio;
using Xunit;

namespace Aetherphone.Tests;

public sealed class AutoGainTests
{
    private const int SampleRate = 48000;
    private const int BlockSamples = 960;
    private const float Frequency = 440f;

    [Fact]
    public void QuietSpeechConvergesTowardTheTarget()
    {
        var gain = new AutoGain();
        var block = new short[BlockSamples];
        var phase = 0;
        var lastToneRms = 0f;
        for (var burst = 0; burst < 12; burst++)
        {
            for (var frame = 0; frame < 15; frame++)
            {
                phase = FillSine(block, 0.0283f, phase);
                gain.Process(block, SampleRate, 1f, true);
                lastToneRms = AutoGain.Rms(block);
            }

            for (var frame = 0; frame < 5; frame++)
            {
                phase = FillSine(block, 0.0007f, phase);
                gain.Process(block, SampleRate, 1f, true);
            }
        }

        var errorDecibels = 20f * MathF.Log10(lastToneRms / AutoGain.TargetRms);
        Assert.InRange(errorDecibels, -2f, 2f);
    }

    [Fact]
    public void LoudSpeechIsBroughtDownWithoutClipping()
    {
        var gain = new AutoGain();
        var block = new short[BlockSamples];
        var phase = 0;
        var inputRms = 0f;
        var outputRms = 0f;
        for (var frame = 0; frame < 100; frame++)
        {
            phase = FillSine(block, 0.95f, phase);
            inputRms = AutoGain.Rms(block);
            gain.Process(block, SampleRate, 1f, true);
            outputRms = AutoGain.Rms(block);
            for (var index = 0; index < block.Length; index++)
            {
                Assert.InRange((int)block[index], -32000, 32000);
            }
        }

        Assert.InRange(gain.Gain, AutoGain.MinimumGain, AutoGain.MinimumGain * 1.02f);
        Assert.True(outputRms < inputRms * 0.6f);
    }

    [Fact]
    public void SilenceIsNeverAmplified()
    {
        var gain = new AutoGain();
        var block = new short[BlockSamples];
        var phase = 0;
        for (var frame = 0; frame < 250; frame++)
        {
            phase = FillSine(block, 0.0014f, phase);
            var before = (short[])block.Clone();
            gain.Process(block, SampleRate, 1f, true);
            Assert.Equal(before, block);
        }

        Array.Clear(block);
        gain.Process(block, SampleRate, 1f, true);
        Assert.All(block, sample => Assert.Equal(0, sample));
        Assert.Equal(1f, gain.Gain);
    }

    [Fact]
    public void GainMovesInBoundedStepsPerBlock()
    {
        var gain = new AutoGain();
        var block = new short[BlockSamples];
        var phase = 0;
        for (var frame = 0; frame < 400; frame++)
        {
            var amplitude = (frame / 25) % 2 == 0 ? 0.9f : 0.02f;
            phase = FillSine(block, amplitude, phase);
            var before = gain.Gain;
            gain.Process(block, SampleRate, 1f, true);
            var stepDecibels = 20f * MathF.Log10(gain.Gain / before);
            Assert.InRange(stepDecibels, -6f, 0.7f);
            Assert.InRange(gain.Gain, AutoGain.MinimumGain, AutoGain.MaximumGain);
        }
    }

    [Fact]
    public void TurnedOffLeavesTheSignalAloneAndReturnsToUnity()
    {
        var gain = new AutoGain();
        var block = new short[BlockSamples];
        var phase = 0;
        for (var frame = 0; frame < 100; frame++)
        {
            phase = FillSine(block, 0.9f, phase);
            gain.Process(block, SampleRate, 1f, true);
        }

        Assert.True(gain.Gain < 1f);
        phase = FillSine(block, 0.3f, phase);
        gain.Process(block, SampleRate, 1f, false);
        Assert.Equal(1f, gain.Gain);
        phase = FillSine(block, 0.3f, phase);
        var before = (short[])block.Clone();
        gain.Process(block, SampleRate, 1f, false);
        Assert.Equal(before, block);
    }

    [Fact]
    public void ManualTrimStillAppliesAndNeverClips()
    {
        var gain = new AutoGain();
        var block = new short[BlockSamples];
        FillSine(block, 0.7f, 0);
        gain.Process(block, SampleRate, 2f, false);
        for (var index = 0; index < block.Length; index++)
        {
            Assert.InRange((int)block[index], -32767, 32767);
        }

        Assert.True(AutoGain.Rms(block) > 0.55f);
    }

    [Fact]
    public void LimiterKeepsEverySampleInsideFullScale()
    {
        Assert.Equal(0, AutoGain.Limit(0f));
        Assert.True(AutoGain.Limit(4f) <= AutoGain.LimiterCeiling * short.MaxValue + 1f);
        Assert.True(AutoGain.Limit(-4f) >= -AutoGain.LimiterCeiling * short.MaxValue - 1f);
        Assert.Equal((short)MathF.Round(0.5f * short.MaxValue), AutoGain.Limit(0.5f));
    }

    private static int FillSine(short[] block, float amplitude, int phase)
    {
        for (var index = 0; index < block.Length; index++)
        {
            var angle = 2f * MathF.PI * Frequency * (phase + index) / SampleRate;
            block[index] = (short)MathF.Round(amplitude * MathF.Sin(angle) * short.MaxValue);
        }

        return phase + block.Length;
    }
}
