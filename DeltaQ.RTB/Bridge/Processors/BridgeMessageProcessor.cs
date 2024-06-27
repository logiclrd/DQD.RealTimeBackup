using System;
using System.Collections.Generic;

using DeltaQ.RTB.Bridge.Messages;

namespace DeltaQ.RTB.Bridge.Processors
{
	public class BridgeMessageProcessor : IBridgeMessageProcessor
	{
		public virtual BridgeMessageType MessageType => BridgeMessageType.Unknown;

		public BridgeMessageProcessor(IEnumerable<IBridgeMessageProcessorImplementation> implementations)
		{
			foreach (var implementation in implementations)
				_processorByMessageType[implementation.MessageType] = implementation;
		}

		Dictionary<BridgeMessageType, IBridgeMessageProcessorImplementation> _processorByMessageType = new Dictionary<BridgeMessageType, IBridgeMessageProcessorImplementation>();

		public bool IsLongRunning(BridgeRequestMessage message)
		{
			if (_processorByMessageType.TryGetValue(message.MessageType, out var processor))
				return processor.IsLongRunning;

			return false;
		}

		public virtual ProcessMessageResult? ProcessMessage(BridgeRequestMessage message)
		{
			if (_processorByMessageType.TryGetValue(message.MessageType, out var processor))
				return processor.ProcessMessage(message);

			return null;
		}
	}
}
