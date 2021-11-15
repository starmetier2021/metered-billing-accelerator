﻿namespace Metering.Types

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Reactive.Linq
open System.Runtime.InteropServices
open Azure.Storage.Blobs
open Azure.Messaging.EventHubs.Consumer
open Metering
open Metering.Types.EventHub

module Aggregator = 
    let handle (partitionEvent: PartitionEvent) = 
        task {
            let lastEnqueuedEvent = partitionEvent.Partition.ReadLastEnqueuedEventProperties()
            let timedelta = lastEnqueuedEvent.LastReceivedTime.Value.Subtract(partitionEvent.Data.EnqueuedTime)
            let sn = partitionEvent.Data.SequenceNumber
            let sequenceDelta = lastEnqueuedEvent.SequenceNumber.Value - sn;
            
            //string readFromPartition = partitionEvent.Partition.PartitionId;
            //byte[] eventBodyBytes = partitionEvent.Data.EventBody.ToArray();
            printf $"partition {partitionEvent.Partition.PartitionId} sequence# {partitionEvent.Data.SequenceNumber} catchup {timedelta} ({sequenceDelta} events)"
            return ()
        }

    // 
    let asyncForeach (handler: ('t -> Task<Unit>)) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) (asncEnumerable: IAsyncEnumerable<'t>) : Task<Unit> =
        task { 
            let asyncEnumerator = asncEnumerable.GetAsyncEnumerator(cancellationToken = cancellationToken)
            
            let! h = asyncEnumerator.MoveNextAsync()
            let mutable hasNext = h
            while hasNext do
                let! _ = handler(asyncEnumerator.Current)

                let! h = asyncEnumerator.MoveNextAsync()
                hasNext <- h

            return ()
        }
    
    //let readEventHub (config: DemoCredential) (someMessagePosition: MessagePosition option) (handler: (PartitionEvent -> Task<Unit>)) =
        //task {
        //    let eventHubConsumerClient = new EventHubConsumerClient(
        //        consumerGroup = config.EventHubInformation.ConsumerGroup,
        //        fullyQualifiedNamespace = $"{config.EventHubInformation.EventHubNamespaceName}.servicebus.windows.net",
        //        eventHubName = config.EventHubInformation.EventHubInstanceName,
        //        credential = config.TokenCredential)

        //    let! ids = eventHubConsumerClient.GetPartitionIdsAsync()            
        //    let firstPartitionId = ids[0]
            
        //    let runtime = TimeSpan.FromSeconds(60)
        //    use cts = new CancellationTokenSource ()
        //    cts.CancelAfter(runtime)
        //    let cancellationToken = cts.Token
        //    printfn $"Start reading across all partitions now for {runtime}"

        //    let readOptions = new ReadEventOptions()
        //    readOptions.TrackLastEnqueuedEventProperties <- true
        //    //readOptions.MaximumWaitTime <- TimeSpan.FromSeconds(2)

        //    let startingPosition =
        //        match someMessagePosition with
        //        | None -> EventPosition.Earliest
        //        | Some p -> EventPosition.FromSequenceNumber(p.SequenceNumber + 1L)
            

        //    let subscribe : Func<IObserver<PartitionEvent>, IDisposable> =
        //        let x (o: IObserver<PartitionEvent>) =
        //            let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        //            let innerCancellationToken = cts.Token

        //            let z = (task {
        //                eventHubConsumerClient.ReadEventsFromPartitionAsync(
        //                    partitionId = "", 
        //                    startingPosition = startingPosition,
        //                    readOptions = readOptions,
        //                    cancellationToken = cts.Token)
        //                |> asyncForeach handler cts.Token 
        //            }) :> Task

                    
        //            let _ = Task.Run(
        //                action = (), 
        //                cancellationToken = innerCancellationToken)
        //            new CancellationDisposable(cts)                
        //        x |> FSharpFuncUtil.Create

        //    let observable = Observable.Create<PartitionEvent>(subscribe)
        //    return ()
        //}
        
    
        
    //let createObservable (snapshotContainerClient: BlobContainerClient) (partitionID: PartitionID) ([<Optional; DefaultParameterValue(CancellationToken())>] cancellationToken: CancellationToken) : Task<IObservable<MeterCollection>> =
    //    task {
    //        let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    //        let! someInitialCollection = MeterCollectionStore.loadLastState snapshotContainerClient partitionID cancellationToken

    //        let someMessagePosition = someInitialCollection |> Option.bind(fun m -> m |> MeterCollection.lastUpdate)

    //        let demo config = 
    //            readEventHub 
    //                config
    //                someMessagePosition
    //                handle

    //        return failwith "not implemented"
    //    }
    