﻿using System;
using System.Collections.Generic;

namespace NSaga
{
    public interface ISaga<TSagaData>
    {
        TSagaData SagaData { get; set; }
        Guid CorrelationId { get; set; }


        /// <summary>
        /// Metadata information
        /// </summary>
        Dictionary<String, String> Headers { get; set; }
    }



    public interface ISagaMessage
    {
        Guid CorrelationId { get; }
    }


    public interface IInitiatingSagaMessage : ISagaMessage
    {
        // marker interface
    }

    public interface InitiatedBy<TMsg> where TMsg : IInitiatingSagaMessage
    {
        OperationResult Initiate(TMsg message);
    }

    public interface ConsumerOf<TMsg> where TMsg : ISagaMessage
    {
        OperationResult Consume(TMsg message);
    }
}
