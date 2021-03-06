﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;


namespace NSaga
{
    public class SagaMediator : ISagaMediator
    {
        private readonly ISagaRepository sagaRepository;
        private readonly IServiceLocator serviceLocator;
        private readonly Assembly[] assembliesToScan;

        public SagaMediator(ISagaRepository sagaRepository, IServiceLocator serviceLocator, params Assembly[] assembliesToScan)
        {
            this.sagaRepository = sagaRepository;
            this.serviceLocator = serviceLocator;

            if (assembliesToScan.Length == 0)
            {
                this.assembliesToScan = AppDomain.CurrentDomain.GetAssemblies();
            }
            else
            {
                this.assembliesToScan = assembliesToScan;
            }
        }


        public OperationResult Consume(ISagaMessage sagaMessage)
        {
            if (sagaMessage.CorrelationId == default(Guid))
            {
                throw new ArgumentException("CorrelationId was not provided in the message. Please make sure you assign CorrelationId before initiating your Saga");
            }

            var sagaTypes = Reflection.GetSagaTypesConsuming(sagaMessage, assembliesToScan);
            if (!sagaTypes.Any())
            {
                throw new ArgumentException($"Message of type {sagaMessage.GetType().Name} is not consumed by any Sagas. Please add ConsumerOf<{sagaMessage.GetType().Name}> to your Saga type");
            }
            if (sagaTypes.Count() > 1)
            {
                // can't have multiple sagas consumed by the same message
                var sagaNames = String.Join(", ", sagaTypes.Select(t => t.Name));
                throw new ArgumentException($"Message of type {sagaMessage.GetType().Name} is consumed by more than one saga. Please make sure any single message is consumed by only one saga. Affected sagas: {sagaNames}");
            }

            var sagaType = sagaTypes.First();
            var saga = Reflection.InvokeGenericMethod(sagaRepository, "Find", sagaType, sagaMessage.CorrelationId);
            if (saga == null)
            {
                throw new ArgumentException($"Saga with this CorrelationId does not exist. Please initiate a saga with IInitiatingMessage.");
            }

            var errors = (OperationResult)Reflection.InvokeMethod(saga, "Consume", sagaMessage);
            sagaRepository.Save(saga); // now the real question - should we persist the saga if operation returned errors? Probably should be configurable

            return errors;
        }


        public OperationResult Consume(IInitiatingSagaMessage initiatingMessage)
        {
            if (initiatingMessage.CorrelationId == default(Guid))
            {
                throw new ArgumentException("CorrelationId was not provided in the message. Please make sure you assign CorrelationId before initiating your Saga");
            }

            // find all sagas that can be initiated by this message
            var sagaTypes = Reflection.GetSagaTypesInitiatedBy(initiatingMessage, assembliesToScan);
            if (!sagaTypes.Any())
            {
                throw new ArgumentException($"Message of type {initiatingMessage.GetType().Name} is not initiating any Sagas. Please add InitiatedBy<{initiatingMessage.GetType().Name}> to your Saga type");
            }
            if (sagaTypes.Count() > 1)
            {
                // can't have multiple sagas initiated by the same message - can't have 2 sagas of different types with the same CorrelationId
                var sagaNames = String.Join(", ", sagaTypes.Select(t => t.Name));
                throw new ArgumentException($"Message of type {initiatingMessage.GetType().Name} is initiating more than one saga. Please make sure any single message is initiating only one saga. Affected sagas: {sagaNames}");
            }

            var sagaType = sagaTypes.First();

            // try to find sagas that already exist
            var existingSaga = Reflection.InvokeGenericMethod(sagaRepository, "Find", sagaType, initiatingMessage.CorrelationId);
            if (existingSaga != null)
            {
                throw new ArgumentException($"Trying to initiate the same saga twice. {initiatingMessage.GetType().Name} is Initiating Message, but saga of type {sagaType.Name} with CorrelationId {initiatingMessage.CorrelationId} already exists");
            }

            // now create an instance of saga and persist the data
            var saga = serviceLocator.Resolve(sagaType);
            Reflection.Set(saga, "CorrelationId", initiatingMessage.CorrelationId);

            // if SagaData is null - create an instance of the object and assign to saga
            var sagaData = Reflection.Get(saga, "SagaData");
            if (sagaData == null)
            {
                var sagaDataType = Reflection.GetInterfaceGenericType(saga, typeof(ISaga<>));
                var newSagaData = Activator.CreateInstance(sagaDataType);
                Reflection.Set(saga, "SagaData", newSagaData);
            }

            var sagaHeaders = Reflection.Get(saga, "Headers");
            if (sagaHeaders == null)
            {
                Reflection.Set(saga, "Headers", new Dictionary<String, String>());
            }

            var errors = (OperationResult)Reflection.InvokeMethod(saga, "Initiate", initiatingMessage);
            sagaRepository.Save(saga); // now the real question - should we persist the saga if operation returned errors? Probably should be configurable

            return errors;
        }
    }
}
