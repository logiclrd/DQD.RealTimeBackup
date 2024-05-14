using System;
using System.Threading;

using NUnit.Framework;

using FluentAssertions;

using Timer = DeltaQ.RTB.Utility.Timer;

namespace DeltaQ.RTB.Tests.Fixtures.Utility
{
	[TestFixture]
	public class TimerTests
	{
		static Random s_rnd = new Random();

		[Test]
		[Repeat(5)]
		public void ScheduleAction_with_TimeSpan_should_work()
		{
			// Arrange
			bool actionExecuted = false;
			Action action = () => { actionExecuted = true; };
			TimeSpan delay = TimeSpan.FromMilliseconds(s_rnd.Next(50, 250));

			var sut = new Timer();

			// Act
			sut.ScheduleAction(delay, action);
			Thread.Sleep(delay - TimeSpan.FromMilliseconds(50));
			bool earlyActionExecuted = actionExecuted;
			Thread.Sleep(TimeSpan.FromMilliseconds(100));

			// Assert
			earlyActionExecuted.Should().BeFalse();
			actionExecuted.Should().BeTrue();
		}

		[Test]
		[Repeat(5)]
		public void ScheduleAction_with_DateTime_should_work()
		{
			// Arrange
			bool actionExecuted = false;
			Action action = () => { actionExecuted = true; };
			TimeSpan delay = TimeSpan.FromMilliseconds(s_rnd.Next(50, 250));
			DateTime deadlineUTC = DateTime.UtcNow + delay;

			var sut = new Timer();

			// Act
			sut.ScheduleAction(deadlineUTC, action);
			Thread.Sleep(delay - TimeSpan.FromMilliseconds(50));
			bool earlyActionExecuted = actionExecuted;
			Thread.Sleep(TimeSpan.FromMilliseconds(100));

			// Assert
			earlyActionExecuted.Should().BeFalse();
			actionExecuted.Should().BeTrue();
		}
	}
}

