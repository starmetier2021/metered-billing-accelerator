﻿namespace Metering.Types

module Json =
    open System
    open Thoth.Json.Net
    open NodaTime.Text

    module MeteringDateTime =
        let private makeEncoder<'T> (pattern : IPattern<'T>) : Encoder<'T> = pattern.Format >> Encode.string
        let private makeDecoder<'T> (pattern : IPattern<'T>) : Decoder<'T> = 
            Decode.string |> Decode.andThen (fun v ->
                let x = pattern.Parse(v)
                if x.Success
                then Decode.succeed x.Value
                else Decode.fail (sprintf "Failed to decode `%s`" v)
        )

        //let instantPattern = InstantPattern.CreateWithInvariantCulture("yyyy-MM-dd--HH-mm-ss-FFF")
        //let private localDatePattern = LocalDatePattern.CreateWithInvariantCulture("yyyy-MM-dd")        
        //let private localTimePattern = LocalTimePattern.CreateWithInvariantCulture("HH:mm")
        //let encodeLocalDate = makeEncoder localDatePattern
        //let decodeLocalDate = makeDecoder localDatePattern
        //let encodeLocalTime = makeEncoder localTimePattern
        //let decodeLocalTime = makeDecoder localTimePattern
        
        // Use the first pattern as default, therefore the `|> List.head`
        let Encoder : Encoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.head |> makeEncoder

        // This supports decoding of multiple formats on how Date and Time could be represented, therefore the `|> Decode.oneOf`
        let Decoder : Decoder<MeteringDateTime> = MeteringDateTime.meteringDateTimePatterns |> List.map makeDecoder |> Decode.oneOf

    module Quantity =
        let Encoder (x: Quantity) : JsonValue = 
            match x with
            | MeteringInt i -> i |> Encode.uint64
            | MeteringFloat f -> f |> Encode.decimal
            | Infinite -> "Infinite" |> Encode.string
            
        let Decoder : Decoder<Quantity> = 
            let decodeInfinite s = 
                match s with
                | "Infinite" -> Infinite |> Decode.succeed
                | invalid -> (sprintf "Failed to decode `%s`" invalid) |> Decode.fail

            [ 
                Decode.uint64 |> Decode.andThen(Quantity.createInt >> Decode.succeed)
                Decode.decimal |> Decode.andThen(Quantity.createFloat >> Decode.succeed)
                Decode.string |> Decode.andThen(decodeInfinite)
            ] |> Decode.oneOf

    module EventHubJSON =
        open Metering.Types.EventHub

        let (partitionId, sequenceNumber, partitionTimestamp) = 
            ("partitionId", "sequenceNumber", "partitionTimestamp")

        let Encoder (x: MessagePosition) : JsonValue =
            [
                (partitionId, x.PartitionID |> Encode.string)
                (sequenceNumber, x.SequenceNumber |> Encode.uint64)
                (partitionTimestamp, x.PartitionTimestamp |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<MessagePosition> =
            Decode.object (fun get -> {
                PartitionID = get.Required.Field partitionId Decode.string
                SequenceNumber = get.Required.Field sequenceNumber Decode.uint64
                PartitionTimestamp = get.Required.Field partitionTimestamp MeteringDateTime.Decoder
            })

    module ConsumedQuantity =
        let (consumedQuantity, created, lastUpdate) = 
            ("consumedQuantity", "created", "lastUpdate")

        let Encoder (x: ConsumedQuantity) : JsonValue =
            [
                (consumedQuantity, x.Amount |> Quantity.Encoder)
                (created, x.Created |> MeteringDateTime.Encoder)
                (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
            ]
            |> Encode.object 

        let Decoder : Decoder<ConsumedQuantity> =
            Decode.object (fun get -> {
                Amount = get.Required.Field consumedQuantity Quantity.Decoder
                Created = get.Required.Field created MeteringDateTime.Decoder
                LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
            })

    module IncludedQuantity =
        let (monthly, annually, created, lastUpdate) =
            ("monthly", "annually", "created", "lastUpdate")

        let Encoder (x: IncludedQuantity) =
            let ts = [ 
                (created, x.Created |> MeteringDateTime.Encoder)
                (lastUpdate, x.LastUpdate |> MeteringDateTime.Encoder)
            ]

            match x with
                | { Monthly = None; Annually = None } -> ts
                | { Monthly = Some m; Annually = None } -> ts |> List.append [ (monthly, m |> Quantity.Encoder) ]
                | { Monthly = None; Annually = Some a} -> ts |> List.append [ (annually, a |> Quantity.Encoder) ]
                | { Monthly = Some m; Annually = Some a } -> ts |> List.append [ (monthly, m |> Quantity.Encoder); (annually, a |> Quantity.Encoder) ]
            |> Encode.object

        let Decoder : Decoder<IncludedQuantity> =
            Decode.object (fun get -> {
                Monthly = get.Optional.Field monthly Quantity.Decoder
                Annually = get.Optional.Field annually Quantity.Decoder
                Created = get.Required.Field created MeteringDateTime.Decoder
                LastUpdate = get.Required.Field lastUpdate MeteringDateTime.Decoder
            })

    module IncludedQuantitySpecification =
        let (monthly, annually) =
            ("monthly", "annually")

        let Encoder (x: IncludedQuantitySpecification) =
            match x with
                | { Monthly = None; Annually = None } -> [ ]
                | { Monthly = Some m; Annually = None } -> [ (monthly, m |> Quantity.Encoder) ]
                | { Monthly = None; Annually = Some a} -> [ (annually, a |> Quantity.Encoder) ]
                | { Monthly = Some m; Annually = Some a } -> [ (monthly, m |> Quantity.Encoder); (annually, a |> Quantity.Encoder) ]
            |> Encode.object

        let Decoder : Decoder<IncludedQuantitySpecification> =
            Decode.object (fun get -> {
                Monthly = get.Optional.Field monthly Quantity.Decoder
                Annually = get.Optional.Field annually Quantity.Decoder
            })
        
    module MeterValue =
        let (consumed, included) =
            ("consumed", "included")

        let Encoder (x: MeterValue) : JsonValue =
            match x with
                | ConsumedQuantity q -> [ ( consumed, q |> ConsumedQuantity.Encoder ) ] 
                | IncludedQuantity q -> [ ( included, q |> IncludedQuantity.Encoder ) ]
            |> Encode.object

        let Decoder : Decoder<MeterValue> =
            [
                Decode.field consumed ConsumedQuantity.Decoder |> Decode.map ConsumedQuantity 
                Decode.field included IncludedQuantity.Decoder |> Decode.map IncludedQuantity
            ] |> Decode.oneOf

    module RenewalInterval =
        let Encoder (x: RenewalInterval) =
            match x with
            | Monthly -> "Monthly" |> Encode.string
            | Annually -> "Annually" |> Encode.string
        
        let Decoder : Decoder<RenewalInterval> =
            Decode.string |> Decode.andThen (
               function
               | "Monthly" -> Decode.succeed Monthly
               | "Annually" -> Decode.succeed Annually
               | invalid -> Decode.fail (sprintf "Failed to decode `%s`" invalid))

    module MarketPlaceAPIJSON =
        open MarketPlaceAPI

        module BillingDimension =
            let (dimensionId, name, unitOfMeasure, includedQuantity) =
                ("dimension", "name", "unitOfMeasure", "includedQuantity");

            let Encoder (x: BillingDimension) : JsonValue =
                [
                    (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
                    (name, x.DimensionName |> Encode.string)
                    (unitOfMeasure, x.UnitOfMeasure |> UnitOfMeasure.value |> Encode.string)
                    (includedQuantity, x.IncludedQuantity |> IncludedQuantitySpecification.Encoder)
                ] |> Encode.object 

            let Decoder : Decoder<BillingDimension> =
                Decode.object (fun get -> {
                    DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                    DimensionName = get.Required.Field name Decode.string
                    UnitOfMeasure = (get.Required.Field unitOfMeasure Decode.string) |> UnitOfMeasure.create
                    IncludedQuantity = get.Required.Field includedQuantity IncludedQuantitySpecification.Decoder
                })
        
        module Plan =
            let (planId, billingDimensions) =
                ("planId", "billingDimensions");

            let Encoder (x: Plan) : JsonValue =
                [
                    (planId, x.PlanId |> PlanId.value |> Encode.string)
                    (billingDimensions, x.BillingDimensions |> Seq.map BillingDimension.Encoder |> Encode.seq)
                ] |> Encode.object 
            
            let Decoder : Decoder<Plan> =
                Decode.object (fun get -> {
                    PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                    BillingDimensions = (get.Required.Field billingDimensions (Decode.list BillingDimension.Decoder)) |> List.toSeq
                })

        module ResourceID =
            open MarketPlaceAPI

            let Encoder (x: ResourceID) : JsonValue =
                match x with
                    | ManagedAppResourceGroupID x -> x |> ManagedAppResourceGroupID.value
                    | SaaSSubscriptionID x ->  x |> SaaSSubscriptionID.value
                |> Encode.string
            
            let Decoder : Decoder<ResourceID> = 
                Decode.string |> Decode.andThen (fun v -> 
                    if v.StartsWith("/subscriptions")
                        then v |> ManagedAppResourceGroupID.create |> ManagedAppResourceGroupID 
                        else v |> SaaSSubscriptionID.create |> SaaSSubscriptionID
                    |> Decode.succeed)

        module MeteredBillingUsageEvent = 
            let (resourceID, quantity, dimensionId, effectiveStartTime, planId) = 
                ("resourceID", "quantity", "dimensionId", "effectiveStartTime", "planId");

            let Encoder (x: MeteredBillingUsageEvent) : JsonValue =
                [
                    (resourceID, x.ResourceID |> ResourceID.Encoder)
                    (quantity, x.Quantity |> Quantity.Encoder)
                    (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
                    (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                    (planId, x.PlanId |> PlanId.value |> Encode.string)
                ] |> Encode.object 
            
            let Decoder : Decoder<MeteredBillingUsageEvent> =
                Decode.object (fun get -> {
                    ResourceID = get.Required.Field resourceID ResourceID.Decoder
                    Quantity = get.Required.Field quantity Quantity.Decoder
                    DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
                    EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                    PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                })

    module SubscriptionType =
        open MarketPlaceAPI
        
        let Encoder =
            SubscriptionType.toStr >> Encode.string
                   
        let Decoder : Decoder<SubscriptionType> = 
            Decode.string |> Decode.andThen(SubscriptionType.fromStr >> Decode.succeed)
      
    module InternalUsageEvent =
        let (timestamp, meterName, quantity, properties, scope) =
            ("timestamp", "meterName", "quantity", "properties", "scope");

        let EncodeMap (x: (Map<string,string> option)) = 
            x
            |> Option.defaultWith (fun () -> Map.empty)
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (k,v) -> (k, v |> Encode.string))
            |> Encode.object

        let DecodeMap : Decoder<Map<string,string>> =
            (Decode.keyValuePairs Decode.string)
            |> Decode.andThen (Map.ofList >> Decode.succeed)

        let Encoder (x: InternalUsageEvent) : JsonValue =
            [
                (scope, x.Scope |> SubscriptionType.Encoder)
                (timestamp, x.Timestamp |> MeteringDateTime.Encoder)
                (meterName, x.MeterName |> ApplicationInternalMeterName.value |> Encode.string)
                (quantity, x.Quantity |> Quantity.Encoder)
                (properties, x.Properties |> EncodeMap)
            ] |> Encode.object 

        let Decoder : Decoder<InternalUsageEvent> =
            Decode.object (fun get -> {
                Scope = get.Required.Field scope SubscriptionType.Decoder
                Timestamp = get.Required.Field timestamp MeteringDateTime.Decoder
                MeterName = (get.Required.Field meterName Decode.string) |> ApplicationInternalMeterName.create
                Quantity = get.Required.Field quantity Quantity.Decoder
                Properties = get.Optional.Field properties DecodeMap
            })

    module Subscription =
        open MarketPlaceAPIJSON
        let (plan, renewalInterval, subscriptionStart, scope) =
            ("plan", "renewalInterval", "subscriptionStart", "scope");

        let Encoder (x: Subscription) : JsonValue =
            [
                (plan, x.Plan |> Plan.Encoder)
                (renewalInterval, x.RenewalInterval |> RenewalInterval.Encoder)
                (subscriptionStart, x.SubscriptionStart |> MeteringDateTime.Encoder)
                (scope, x.SubscriptionType |> SubscriptionType.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<Subscription> =
            Decode.object (fun get -> {
                Plan = get.Required.Field plan Plan.Decoder
                RenewalInterval = get.Required.Field renewalInterval RenewalInterval.Decoder
                SubscriptionStart = get.Required.Field subscriptionStart MeteringDateTime.Decoder
                SubscriptionType = get.Required.Field scope SubscriptionType.Decoder
            })

    module PlanDimension =
        open MarketPlaceAPI

        let (planId, dimensionId) =
            ("plan", "dimension");

        let Encoder (x: PlanDimension) : JsonValue =
            [
                (planId, x.PlanId |> PlanId.value |> Encode.string)
                (dimensionId, x.DimensionId |> DimensionId.value |> Encode.string)
            ] |> Encode.object 
        
        let Decoder : Decoder<PlanDimension> =
            Decode.object (fun get -> {
                PlanId = (get.Required.Field planId Decode.string) |> PlanId.create
                DimensionId = (get.Required.Field dimensionId Decode.string) |> DimensionId.create
            })

    module InternalMetersMapping =
        open MarketPlaceAPI
        let Encoder (x: InternalMetersMapping) = 
            x
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (k, v) -> (k |> ApplicationInternalMeterName.value, v |> DimensionId.value |> Encode.string))
            |> Encode.object

        let Decoder : Decoder<InternalMetersMapping> =
            (Decode.keyValuePairs Decode.string)
            |> Decode.andThen (fun r -> r |> List.map (fun (k, v) -> (k |> ApplicationInternalMeterName.create, v |> DimensionId.create)) |> Map.ofList |> Decode.succeed)
        
    module CurrentMeterValues = 
        open MarketPlaceAPI

        let Encoder (x: CurrentMeterValues) = 
            x
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (dimensionId, meterValue) -> 
                [
                    ("dimensionId", dimensionId |> DimensionId.value |> Encode.string)
                    ("meterValue", meterValue |> MeterValue.Encoder)
                ]
                |> Encode.object)
            |> Encode.list

        let Decoder : Decoder<CurrentMeterValues> =            
            Decode.list (Decode.object (fun get -> 
                let dimensionId = get.Required.Field "dimensionId" Decode.string  
                let meterValue = get.Required.Field "meterValue" MeterValue.Decoder  
                (dimensionId, meterValue)
            ))
            |> Decode.andThen  (fun r -> r |> List.map(fun (k, v) -> (k |> DimensionId.create, v)) |> Map.ofList |> Decode.succeed)

    module MeteringAPIUsageEventDefinition = 
        open MarketPlaceAPIJSON
        let (resourceId, quantity, planDimension, effectiveStartTime, scope) =
            ("resourceId", "quantity", "planDimension", "effectiveStartTime", "scope");

        let Encoder (x: MeteringAPIUsageEventDefinition) : JsonValue =
            [
                (resourceId, x.ResourceId |> ResourceID.Encoder)
                (quantity, x.Quantity |> Encode.decimal)
                (planDimension, x.PlanDimension |> PlanDimension.Encoder)
                (effectiveStartTime, x.EffectiveStartTime |> MeteringDateTime.Encoder)
                (scope, x.SubscriptionType |> SubscriptionType.Encoder)
            ] |> Encode.object 
        
        let Decoder : Decoder<MeteringAPIUsageEventDefinition> =
            Decode.object (fun get -> {
                ResourceId = get.Required.Field resourceId ResourceID.Decoder
                Quantity = get.Required.Field quantity Decode.decimal
                PlanDimension = get.Required.Field planDimension PlanDimension.Decoder
                EffectiveStartTime = get.Required.Field effectiveStartTime MeteringDateTime.Decoder
                SubscriptionType = get.Required.Field scope SubscriptionType.Decoder
            })
    
    module SubscriptionCreationInformation =
        open MarketPlaceAPIJSON

        let (subscription, metersMapping) =
            ("subscription", "metersMapping");

        let Encoder (x: SubscriptionCreationInformation) : JsonValue =
            [
                (subscription, x.Subscription |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<SubscriptionCreationInformation> =
            Decode.object (fun get -> {
                Subscription = get.Required.Field subscription Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
            })

    module Meter =
        open MarketPlaceAPIJSON
        
        let (subscription, metersMapping, currentMeters, usageToBeReported, lastProcessedMessage) =
            ("subscription", "metersMapping", "currentMeters", "usageToBeReported", "lastProcessedMessage");

        let Encoder (x: Meter) : JsonValue =
            [
                (subscription, x.Subscription |> Subscription.Encoder)
                (metersMapping, x.InternalMetersMapping |> InternalMetersMapping.Encoder)
                (currentMeters, x.CurrentMeterValues |> CurrentMeterValues.Encoder)
                (usageToBeReported, x.UsageToBeReported |> List.map MeteringAPIUsageEventDefinition.Encoder |> Encode.list)
                (lastProcessedMessage, x.LastProcessedMessage |> EventHubJSON.Encoder)
            ] |> Encode.object 

        let Decoder : Decoder<Meter> =
            Decode.object (fun get -> {
                Subscription = get.Required.Field subscription Subscription.Decoder
                InternalMetersMapping = get.Required.Field metersMapping InternalMetersMapping.Decoder
                CurrentMeterValues = get.Required.Field currentMeters CurrentMeterValues.Decoder
                UsageToBeReported = get.Required.Field usageToBeReported (Decode.list MeteringAPIUsageEventDefinition.Decoder)
                LastProcessedMessage = get.Required.Field lastProcessedMessage EventHubJSON.Decoder
            })

    module MeterCollection =
        open MarketPlaceAPI

        let Encoder (x: MeterCollection) = 
            x
            |> Map.toSeq |> Seq.toList
            |> List.map (fun (k, v) -> (k |> SubscriptionType.toStr, v |> Meter.Encoder))
            |> Encode.object

        let Decoder : Decoder<MeterCollection> =
            let turnKeyIntoSubscriptionType (k, v) =
                (k |> SubscriptionType.fromStr, v)

            (Decode.keyValuePairs Meter.Decoder)
            |> Decode.andThen (fun r -> r |> List.map turnKeyIntoSubscriptionType  |> Map.ofList |> Decode.succeed)

    module MeteringUpdateEvent =
        let (typeid, value) =
            ("type", "value");

        let Encoder (x: MeteringUpdateEvent) : JsonValue =
            match x with
            | SubscriptionPurchased sub -> 
                [
                     (typeid, "subscriptionPurchased" |> Encode.string)
                     (value, sub |> SubscriptionCreationInformation.Encoder)
                ]
            | UsageReported usage ->
                [
                     (typeid, "usage" |> Encode.string)
                     (value, usage |> InternalUsageEvent.Encoder)
                ]
            | UsageSubmittedToAPI usage -> raise <| new NotSupportedException "Currently this feedback loop must only be internally"
            | AggregatorBooted -> raise <| new NotSupportedException "Currently this feedback loop must only be internally"
            |> Encode.object 
            
        let Decoder : Decoder<MeteringUpdateEvent> =
            Decode.object (fun get ->                
                match (get.Required.Field typeid Decode.string) with
                | "subscriptionPurchased" -> (get.Required.Field value SubscriptionCreationInformation.Decoder) |> SubscriptionPurchased
                | "usage" -> (get.Required.Field value InternalUsageEvent.Decoder) |> UsageReported
                | invalidType  -> failwithf "`%s` is not a valid type" invalidType
            )

    open MarketPlaceAPIJSON
    
    let enrich x =
        x
        |> Extra.withUInt64
        |> Extra.withCustom Quantity.Encoder Quantity.Decoder
        |> Extra.withCustom MeteringDateTime.Encoder MeteringDateTime.Decoder
        |> Extra.withCustom EventHubJSON.Encoder EventHubJSON.Decoder
        |> Extra.withCustom IncludedQuantitySpecification.Encoder IncludedQuantitySpecification.Decoder
        |> Extra.withCustom ConsumedQuantity.Encoder ConsumedQuantity.Decoder
        |> Extra.withCustom IncludedQuantity.Encoder IncludedQuantity.Decoder
        |> Extra.withCustom MeterValue.Encoder MeterValue.Decoder
        |> Extra.withCustom RenewalInterval.Encoder RenewalInterval.Decoder
        |> Extra.withCustom BillingDimension.Encoder BillingDimension.Decoder
        |> Extra.withCustom Plan.Encoder Plan.Decoder
        |> Extra.withCustom MeteredBillingUsageEvent.Encoder MeteredBillingUsageEvent.Decoder
        |> Extra.withCustom InternalUsageEvent.Encoder InternalUsageEvent.Decoder
        |> Extra.withCustom Subscription.Encoder Subscription.Decoder
        |> Extra.withCustom PlanDimension.Encoder PlanDimension.Decoder
        |> Extra.withCustom InternalMetersMapping.Encoder InternalMetersMapping.Decoder
        |> Extra.withCustom CurrentMeterValues.Encoder CurrentMeterValues.Decoder
        |> Extra.withCustom MeteringAPIUsageEventDefinition.Encoder MeteringAPIUsageEventDefinition.Decoder
        |> Extra.withCustom SubscriptionCreationInformation.Encoder SubscriptionCreationInformation.Decoder
        |> Extra.withCustom Meter.Encoder Meter.Decoder
        |> Extra.withCustom MeteringUpdateEvent.Encoder MeteringUpdateEvent.Decoder
        |> Extra.withCustom MeterCollection.Encoder MeterCollection.Decoder

    let enriched = Extra.empty |> enrich

    let toStr o = Encode.Auto.toString(4, o, extra = enriched)
        
    let fromStr<'T> json = 
        match Decode.Auto.fromString<'T>(json, extra = enriched) with
        | Ok r -> r
        | Result.Error e -> failwith e
    