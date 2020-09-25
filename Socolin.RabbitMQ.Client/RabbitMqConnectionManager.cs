using System;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace Socolin.RabbitMQ.Client
{
	public enum ChannelType
	{
		Publish,
		Consumer
	}

	public interface IRabbitMqConnectionManager : IDisposable
	{
		Task<ChannelContainer> AcquireChannel(ChannelType channelType);
		void ReleaseChannel(ChannelType channelType, IModel channel);
	}

	public class RabbitMqConnectionManager : IRabbitMqConnectionManager
	{
		private readonly IRabbitMqChannelManager _publishChannelManager;
		private readonly IRabbitMqChannelManager _consumerChannelManager;

		public RabbitMqConnectionManager(Uri uri, string connectionName, TimeSpan connectionTimeout)
		{
			_publishChannelManager = new RabbitMqChannelManager(uri, connectionName, connectionTimeout, ChannelType.Publish);
			_consumerChannelManager = new RabbitMqChannelManager(uri, connectionName, connectionTimeout, ChannelType.Consumer);
		}

		public void Dispose()
		{
			_publishChannelManager.Dispose();
			_consumerChannelManager.Dispose();
		}

		public Task<ChannelContainer> AcquireChannel(ChannelType channelType)
		{
			switch (channelType)
			{
				case ChannelType.Publish:
					return _publishChannelManager.AcquireChannel();
				case ChannelType.Consumer:
					return _consumerChannelManager.AcquireChannel();
				default:
					throw new ArgumentOutOfRangeException(nameof(channelType), channelType, null);
			}
		}

		public void ReleaseChannel(ChannelType channelType, IModel channel)
		{
			switch (channelType)
			{
				case ChannelType.Publish:
					_publishChannelManager.ReleaseChannel(channel);
					break;
				case ChannelType.Consumer:
					_consumerChannelManager.ReleaseChannel(channel);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(channelType), channelType, null);
			}
		}
	}
}