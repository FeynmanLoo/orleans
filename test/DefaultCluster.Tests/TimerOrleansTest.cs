﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using Tester;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace DefaultCluster.Tests.TimerTests
{
    public class TimerOrleansTest : HostedTestClusterEnsureDefaultStarted
    {
        private readonly ITestOutputHelper output;

        public TimerOrleansTest(ITestOutputHelper output, DefaultClusterFixture fixture)
            : base(fixture)
        {
            this.output = output;
        }

        [Fact, TestCategory("BVT"), TestCategory("Functional"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Basic()
        {
            for (int i = 0; i < 10; i++)
            {
                var grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
                var period = await grain.GetTimerPeriod();
                var timeout = period.Multiply(50);
                var stopwatch = Stopwatch.StartNew();
                var last = 0;
                while (stopwatch.Elapsed < timeout && last < 10)
                {
                    await Task.Delay(period.Divide(2));
                    last = await grain.GetCounter();
                }

                output.WriteLine("value = " + last);
                Assert.True(last >= 10 & last <= 12, last.ToString());

                await grain.StopDefaultTimer();
                await Task.Delay(period.Multiply(2));
                var curr = await grain.GetCounter();
                Assert.True(curr == last || curr == last + 1, "curr == last || curr == last + 1");
            }
        }

        [Fact, TestCategory("BVT"), TestCategory("Functional"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Parallel()
        {
            TimeSpan period = TimeSpan.Zero;
            List<ITimerGrain> grains = new List<ITimerGrain>();
            for (int i = 0; i < 10; i++)
            {
                ITimerGrain grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
                grains.Add(grain);
                period = await grain.GetTimerPeriod(); // activate grains
            }

            var tasks = new List<Task>(grains.Count);
            for (int i = 0; i < grains.Count; i++)
            {
                ITimerGrain grain = grains[i];
                tasks.Add(
                    Task.Run(
                        async () =>
                        {
                            int last = await grain.GetCounter();
                            var stopwatch = Stopwatch.StartNew();
                            var timeout = period.Multiply(50);
                            while (stopwatch.Elapsed < timeout && last < 10)
                            {
                                await Task.Delay(period.Divide(2));
                                last = await grain.GetCounter();
                            }

                            output.WriteLine("value = " + last);
                            Assert.True(last >= 10 && last <= 12, "last >= 10 && last <= 12");
                        }));
            }

            await Task.WhenAll(tasks);
            for (int i = 0; i < grains.Count; i++)
            {
                ITimerGrain grain = grains[i];
                await grain.StopDefaultTimer();
            }
        }


        [Fact, TestCategory("BVT"), TestCategory("Functional"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Migration()
        {
            ITimerGrain grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
            TimeSpan period = await grain.GetTimerPeriod();

            // Ensure that the grain works as it should.
            var last = await grain.GetCounter();
            var stopwatch = Stopwatch.StartNew();
            var timeout = period.Multiply(50);
            while (stopwatch.Elapsed < timeout && last < 10)
            {
                await Task.Delay(period.Divide(2));
                last = await grain.GetCounter();
            }

            last = await grain.GetCounter();
            output.WriteLine("value = " + last);
            Assert.True(last >= 10 && last <= 12, $"last >= 10 && last <= 12. Actual: last = {last}");

            // Restart the silo.
            HostedCluster.RestartSilo(HostedCluster.Primary);
            stopwatch.Restart();
            period = await grain.GetTimerPeriod();

            // Poke the grain and ensure it still works as it should.
            last = await grain.GetCounter();
            while (stopwatch.Elapsed < timeout && last < 10)
            {
                await Task.Delay(period.Divide(2));
                last = await grain.GetCounter();
            }

            last = await grain.GetCounter();
            Assert.True(last >= 10 && last <= 12, $"last >= 10 && last <= 12. Actual: last = {last}");
            double maximalNumTicks = stopwatch.Elapsed.Divide(period);
            Assert.True(
                last <= maximalNumTicks,
                $"Assert: last <= maximalNumTicks. Actual: last = {last}, maximalNumTicks = {maximalNumTicks}");
            //mgmtGrain.ResumeHost(Orleans.SiloAddress).Wait();
            output.WriteLine(
                "Total Elaped time = " + (stopwatch.Elapsed.TotalSeconds) + " sec. Expected Ticks = " + maximalNumTicks +
                ". Actual ticks = " + last);
        }

        [Fact, TestCategory("BVT"), TestCategory("Functional"), TestCategory("Timers")]
        public async Task AsyncTimerTest_GrainCall()
        {
            const string testName = "AsyncTimerTest_GrainCall";
            TimeSpan delay = TimeSpan.FromSeconds(5);
            TimeSpan wait = delay.Multiply(2);

            ITimerCallGrain grain = null;

            Exception error = null;
            try
            {
                grain = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());

                await grain.StartTimer(testName, delay);

                await Task.Delay(wait);

                int tickCount = await grain.GetTickCount();
                Assert.Equal(1, tickCount);

                Exception err = await grain.GetException();
                Assert.Null(err); // Should be no exceptions during timer callback
            }
            catch (Exception exc)
            {
                output.WriteLine(exc);
                error = exc;
            }

            try
            {
                if (grain != null) await grain.StopTimer(testName);
            }
            catch (Exception exc)
            {
                // Ignore
                output.WriteLine("Ignoring exception from StopTimer : {0}", exc);
            }

            if (error != null)
            {
                Assert.True(false, $"Test {testName} failed with error {error}");
            }
        }
    }
}
