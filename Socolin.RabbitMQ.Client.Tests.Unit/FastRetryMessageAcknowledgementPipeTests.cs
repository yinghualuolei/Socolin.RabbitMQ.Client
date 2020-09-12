using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Socolin.RabbitMQ.Client.ConsumerPipes;
using Socolin.RabbitMQ.Client.ConsumerPipes.Context;

namespace Socolin.RabbitMQ.Client.Tests.Unit
{
	public class FastRetryMessageAcknowledgementPipeTests
	{
		private const ulong DeliveryTag = 27972ul;
		private const string RoutingKey = "some-routing-key";
		private static readonly byte[] Body = {0x42, 0x66};

		private FastRetryMessageAcknowledgementPipe<string> _pipe;

		private ConsumerPipeContext<string> _consumerPipeContext;
		private Mock<IModel> _channelMock;
		private Mock<IConsumerPipe<string>> _nextPipeMock;
		private ReadOnlyMemory<IConsumerPipe<string>> _fakePipeline;
		private BasicDeliverEventArgs _basicDeliverEventArgs;
		private FakeBasicProperties _basicProperties;

		[SetUp]
		public void Setup()
		{
			_pipe = new FastRetryMessageAcknowledgementPipe<string>(2);

			_nextPipeMock = new Mock<IConsumerPipe<string>>(MockBehavior.Strict);
			_fakePipeline = new ReadOnlyMemory<IConsumerPipe<string>>(new[] {_nextPipeMock.Object});
			_basicProperties = new FakeBasicProperties();

			_basicDeliverEventArgs = new BasicDeliverEventArgs
			{
				DeliveryTag = DeliveryTag,
				RoutingKey = RoutingKey,
				Body = Body,
				BasicProperties = _basicProperties
			};
			_channelMock = new Mock<IModel>(MockBehavior.Strict);
			_consumerPipeContext = new ConsumerPipeContext<string>(_channelMock.Object, _basicDeliverEventArgs, (items, message) => Task.CompletedTask);
		}

		[Test]
		public void ProcessAsync_WhenItWorks_AckTheMessage()
		{
			_channelMock.Setup(m => m.BasicAck(DeliveryTag, false))
				.Verifiable();
			_nextPipeMock.Setup(m => m.ProcessAsync(_consumerPipeContext, It.IsAny<ReadOnlyMemory<IConsumerPipe<string>>>()))
				.Returns(Task.CompletedTask);

			_pipe.ProcessAsync(_consumerPipeContext, _fakePipeline);

			_channelMock.Verify(m => m.BasicAck(DeliveryTag, false), Times.Once);
		}

		[Test]
		public void ProcessAsync_WhenProcessFail_AddHeaderOnMessageAndRepublishIt_AndAcknowledgeCurrentMessage()
		{
			var s = new MockSequence();
			_channelMock.InSequence(s).Setup(m => m.BasicPublish(string.Empty, RoutingKey, true, _basicProperties, Body));
			_channelMock.InSequence(s).Setup(m => m.BasicAck(DeliveryTag, false));

			_nextPipeMock.Setup(m => m.ProcessAsync(_consumerPipeContext, It.IsAny<ReadOnlyMemory<IConsumerPipe<string>>>()))
				.Returns(Task.FromException(new Exception("forced")));

			_pipe.ProcessAsync(_consumerPipeContext, _fakePipeline);

			Assert.That(_basicProperties.Headers.ContainsKey("RetryCount"), Is.True);
			Assert.That(_basicProperties.Headers["RetryCount"], Is.EqualTo(1));
			_channelMock.Verify(m => m.BasicPublish(string.Empty, RoutingKey, true, _basicProperties, Body), Times.Once);
			_channelMock.Verify(m => m.BasicAck(DeliveryTag, false), Times.Once);
		}


		[Test]
		public void ProcessAsync_WhenProcessFail_IncrementRetryCount_AndRepublishMessageAndAckCurrentMessage()
		{
			var s = new MockSequence();
			_channelMock.InSequence(s).Setup(m => m.BasicPublish(string.Empty, RoutingKey, true, _basicProperties, Body));
			_channelMock.InSequence(s).Setup(m => m.BasicAck(DeliveryTag, false));

			_basicProperties.Headers = new Dictionary<string, object> {["RetryCount"] = 1};
			_nextPipeMock.Setup(m => m.ProcessAsync(_consumerPipeContext, It.IsAny<ReadOnlyMemory<IConsumerPipe<string>>>()))
				.Returns(Task.FromException(new Exception("forced")));

			_pipe.ProcessAsync(_consumerPipeContext, _fakePipeline);

			Assert.That(_basicProperties.Headers["RetryCount"], Is.EqualTo(2));
			_channelMock.Verify(m => m.BasicPublish(string.Empty, RoutingKey, true, _basicProperties, Body), Times.Once);
			_channelMock.Verify(m => m.BasicAck(DeliveryTag, false), Times.Once);
		}

		[Test]
		public void ProcessAsync_WhenProcessFail_RejectMessageAfterTooManyRetry()
		{
			_channelMock.Setup(m => m.BasicReject(DeliveryTag, false))
				.Verifiable();

			_basicProperties.Headers = new Dictionary<string, object> {["RetryCount"] = 2};
			_nextPipeMock.Setup(m => m.ProcessAsync(_consumerPipeContext, It.IsAny<ReadOnlyMemory<IConsumerPipe<string>>>()))
				.Returns(Task.FromException(new Exception("forced")));

			_pipe.ProcessAsync(_consumerPipeContext, _fakePipeline);

			_channelMock.Verify(m => m.BasicReject(DeliveryTag, false), Times.Once);
		}
	}
}