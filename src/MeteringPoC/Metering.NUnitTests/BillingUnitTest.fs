module Metering.NUnitTests.Billing

open System
open NUnit.Framework
open Metering
open Metering.Types
open Metering.BillingPeriod
open Metering.Types.MarketPlaceAPI
open Metering.Logic
open NodaTime

[<SetUp>]
let Setup () = ()

let d = MeteringDateTime.fromStr

let bp (s: string) : BillingPeriod =
    s.Split([|'|'|], 3)
    |> Array.toList
    |> List.map (fun s -> s.Trim())
    |> function
        | [indx; startVal; endVal] -> { Start = (d startVal); End = (d endVal); Index = (uint (Int64.Parse(indx))) }
        | _ -> failwith "parsing error"

let runTestVectors test testcases = testcases |> List.indexed |> List.map test |> ignore

let somePlan : Plan = 
    { PlanId = "PlanId"
      BillingDimensions = Seq.empty }

[<Test>]
let Test_BillingPeriod_createFromIndex () =
    let sub = Subscription.create somePlan Monthly (d "2021-05-13--12-00-03") 

    Assert.AreEqual(
        (bp "2|2021-07-13--12-00-03|2021-08-12--12-00-02"),
        (BillingPeriod.createFromIndex sub 2u))

type Subscription_determineBillingPeriod_Vector = { Purchase: (RenewalInterval * string); Expected: string; Candidate: string}
        
[<Test>]
let Test_BillingPeriod_determineBillingPeriod () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan interval (d purchaseDateStr)
        let expected : Result<BillingPeriod, BusinessError> = Ok(bp testcase.Expected)
        let compute = BillingPeriod.determineBillingPeriod subscription (d testcase.Candidate)
        Assert.AreEqual(expected, compute, sprintf "Failure test case %d expected=%A but was %A" idx expected compute);

    [
        {Purchase=(Monthly, "2021-05-13--12-00-00"); Candidate="2021-05-30--12-00-00"; Expected="0|2021-05-13--12-00-00|2021-06-12--11-59-59"}
        {Purchase=(Monthly, "2021-05-13--12-00-00"); Candidate="2021-08-01--12-00-00"; Expected="2|2021-07-13--12-00-00|2021-08-12--11-59-59"}
        // if I purchase on the 29th of Feb in a leap year, 
        // my billing renews on 28th of Feb the next year, 
        // therefore last day of the current billing period is 27th next year
        {Purchase=(Annually, "2004-02-29--12-00-00"); Candidate="2004-03-29--12-00-00"; Expected="0|2004-02-29--12-00-00|2005-02-27--11-59-59"}
        {Purchase=(Annually, "2021-05-13--12-00-00"); Candidate="2021-08-01--12-00-00"; Expected="0|2021-05-13--12-00-00|2022-05-12--11-59-59"}
        {Purchase=(Annually, "2021-05-13--12-00-00"); Candidate="2022-08-01--12-00-00"; Expected="1|2022-05-13--12-00-00|2023-05-12--11-59-59"}
    ] |> runTestVectors test

type BillingPeriod_isInBillingPeriod_Vector = { Purchase: (RenewalInterval * string); BillingPeriodIndex: uint; Candidate: string; Expected: bool}

[<Test>]
let Test_BillingPeriod_isInBillingPeriod () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan interval (d purchaseDateStr)
        let billingPeriod = testcase.BillingPeriodIndex |> BillingPeriod.createFromIndex subscription 
        let result = (d testcase.Candidate) |> BillingPeriod.isInBillingPeriod billingPeriod 
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        { Purchase=(Monthly, "2021-05-13--12-00-00"); BillingPeriodIndex=3u; Candidate="2021-08-13--12-00-00"; Expected=true}
        { Purchase=(Monthly, "2021-05-13--12-00-00"); BillingPeriodIndex=3u; Candidate="2021-08-15--12-00-00"; Expected=true}
        { Purchase=(Monthly, "2021-05-13--12-00-00"); BillingPeriodIndex=3u; Candidate="2021-09-12--11-59-59"; Expected=true}
        { Purchase=(Monthly, "2021-05-13--12-00-00"); BillingPeriodIndex=3u; Candidate="2021-09-13--12-00-00"; Expected=false}
        { Purchase=(Monthly, "2021-05-13--12-00-00"); BillingPeriodIndex=4u; Candidate="2021-09-13--12-00-00"; Expected=true}
    ] |> runTestVectors test

type BillingPeriod_getBillingPeriodDelta_Vector = { Purchase: (RenewalInterval * string); Previous: string; Current: string; Expected: BillingPeriodResult}

[<Test>]
let Test_BillingPeriod_getBillingPeriodDelta () =
    let test (idx, testcase) =
        let (interval, purchaseDateStr) = testcase.Purchase
        let subscription = Subscription.create somePlan interval (d purchaseDateStr)
        let result = (BillingPeriod.getBillingPeriodDelta subscription (d testcase.Previous) (d testcase.Current))
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)

    [
        { Purchase=(Monthly,"2021-05-13--12-00-00"); Previous="2021-05-13--12-00-00"; Current="2021-05-13--12-00-00"; Expected=SameBillingPeriod }
        { Purchase=(Monthly,"2021-05-13--12-00-00"); Previous="2021-08-13--12-00-00"; Current="2021-08-15--12-00-00"; Expected=SameBillingPeriod }
        { Purchase=(Monthly,"2021-08-17--12-00-00"); Previous="2021-08-17--12-00-00"; Current="2021-09-12--12-00-00"; Expected=SameBillingPeriod }
        
        // When the second flips, a new BillingPeriod starts
        { Purchase=(Monthly,"2021-05-13--12-00-00"); Previous="2021-08-17--12-00-00"; Current="2021-09-13--11-59-59"; Expected=SameBillingPeriod }
        { Purchase=(Monthly,"2021-05-13--12-00-00"); Previous="2021-08-17--12-00-00"; Current="2021-09-13--12-00-00"; Expected=1u |> BillingPeriodsAgo}
        
        { Purchase=(Monthly,"2021-05-13--12-00-00"); Previous="2021-08-17--12-00-00"; Current="2021-10-13--12-00-00"; Expected=2u |> BillingPeriodsAgo }
    ] |> runTestVectors test

type MeterValue_subtractQuantityFromMeterValue_Vector = { State: MeterValue; Quantity: Quantity; Expected: MeterValue}
[<Test>]
let Test_Logic_subtractQuantityFromMeterValue() =
    let created = "2021-10-28--11-38-00" |> MeteringDateTime.fromStr
    let lastUpdate = "2021-10-28--11-38-00" |> MeteringDateTime.fromStr
    let now = "2021-10-28--11-38-00" |> MeteringDateTime.fromStr
    let test (idx, testcase) = 
        let result = Logic.subtractQuantityFromMeterValue now testcase.State testcase.Quantity
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)
    
    [ 
        {
            // if Monthly is sufficient, don't touch annual
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL; Created = created; LastUpdate = lastUpdate }
            Quantity = 8UL
            Expected = IncludedQuantity { Annually = Some 30UL; Monthly = Some 2UL; Created = created; LastUpdate = now }
        }
        {
            // if Monthly is not sufficient, also deduct from annual
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL; Created = created; LastUpdate = lastUpdate}
            Quantity = 13UL
            Expected = IncludedQuantity { Annually = Some 27UL; Monthly = None; Created = created; LastUpdate = now}
        }
        {
            // if both Monthly and Annual are not sufficient, it costs money
            State = IncludedQuantity { Annually = Some 30UL; Monthly = Some 10UL; Created = created; LastUpdate = lastUpdate }
            Quantity = 43UL
            Expected = ConsumedQuantity { Amount = 3UL; Created = created; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = IncludedQuantity { Annually = None; Monthly = None; Created = created; LastUpdate = lastUpdate }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 2UL; Created = created; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = 0UL; Created = created; LastUpdate = lastUpdate }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 2UL; Created = created; LastUpdate = now }
        }
        {
            // If there's nothing, it costs money
            State = ConsumedQuantity { Amount = 10UL; Created = created; LastUpdate = lastUpdate }
            Quantity = 2UL
            Expected = ConsumedQuantity { Amount = 12UL; Created = created; LastUpdate = now }
        }
    ] |> runTestVectors test

type MeterValue_topupMonthlyCredits_Vector = { Input: MeterValue; Values: (Quantity * RenewalInterval) list; Expected: MeterValue}

[<Test>]
let Test_Logic_topupMonthlyCredits() =
    let created = "2021-10-28--11-38-00" |> MeteringDateTime.fromStr
    let lastUpdate = "2021-10-28--11-38-00" |> MeteringDateTime.fromStr
    let now = "2021-10-28--11-38-00" |> MeteringDateTime.fromStr

    let test (idx, testcase) =
        let result = testcase.Values |> List.fold (Logic.topupMonthlyCredits |> (fun f a (b, c) -> a |> f now b c)) testcase.Input
        Assert.AreEqual(testcase.Expected, result, sprintf "Failure test case %d" idx)
    
    [
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = None; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Some 1UL; Monthly = Some 9UL; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Monthly)]
            Expected = IncludedQuantity { Annually = Some 1UL; Monthly = Some 9UL; Created = created; LastUpdate = now } 
        }
        {
            Input = ConsumedQuantity { Amount = 100_000UL; Created = created; LastUpdate = lastUpdate }
            Values = [(1000UL, Monthly)]
            Expected = IncludedQuantity { Annually = None; Monthly = Some 1000UL; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = None; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 9UL; Monthly = None; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL; Created = created; LastUpdate = lastUpdate } 
            Values = [(9UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 9UL ; Monthly = Some 2UL; Created = created; LastUpdate = now } 
        }
        {
            Input = ConsumedQuantity { Amount = 100_000UL; Created = created; LastUpdate = lastUpdate }
            Values = [(1000UL, Annually)]
            Expected = IncludedQuantity { Annually = Some 1000UL ; Monthly = None; Created = created; LastUpdate = now } 
        }
        {
            Input = IncludedQuantity { Annually = Some 1UL; Monthly = Some 2UL; Created = created; LastUpdate = lastUpdate } 
            Values = [
                (10_000UL, Annually)
                (500UL, Monthly)
            ]
            Expected = IncludedQuantity { Annually = Some 10_000UL; Monthly = Some 500UL; Created = created; LastUpdate = now } 
        }
    ] |> runTestVectors test

[<Test>]
let Test_previousBillingIntervalCanBeClosedNewEvent() =
    let test (idx, (prev, curEv, expected)) =
        let result : CloseBillingPeriod = 
            Logic.previousBillingIntervalCanBeClosedNewEvent
                (prev |> MeteringDateTime.fromStr)
                (curEv |> MeteringDateTime.fromStr)
        
        Assert.AreEqual(expected, result, sprintf "Failure test case #%d" idx)

    [
        ("2021-01-10--11-59-58", "2021-01-10--11-59-59", KeepOpen) // Even though we're already 10 seconds in the new hour, the given event belongs to the previous hour, so there might be more
        ("2021-01-10--11-59-58", "2021-01-10--12-00-00", Close) // The event belongs to a new period, so close it        
        ("2021-01-10--12-00-00", "2021-01-11--12-00-00", Close) // For whatever reason, we've been sleeping for exactly one day
    ] |> runTestVectors test

[<Test>]
let Test_previousBillingIntervalCanBeClosedWakeup() =
    let test (idx, (prev, curr, (grace: float), expected)) =
        let config =
            { CurrentTimeProvider = curr |> MeteringDateTime.fromStr |> CurrentTimeProvider.AlwaysReturnSameTime
              SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
              GracePeriod = Duration.FromHours(grace) }

        let result = prev |> MeteringDateTime.fromStr |> Logic.previousBillingIntervalCanBeClosedWakeup config  
                
        Assert.AreEqual(expected, result, sprintf "Failure testc case #%d" idx)

    [
        ("2021-01-10--12-00-00", "2021-01-10--14-59-59", 3.0, KeepOpen)
        ("2021-01-10--12-00-00", "2021-01-10--15-00-00", 3.0, Close)
    ] |> runTestVectors test

