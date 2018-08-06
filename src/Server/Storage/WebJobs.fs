namespace ServerCode.Storage

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

// /// Contains all reactive web jobs as required by the application.
// type AnnotationsWebJobs(connectionString) =
//     member __.ClearAnnotationssWebJob([<TimerTrigger "00:10:00">] timer:TimerInfo) =
//         AzureTable.clearAnnotationss connectionString

// /// An extremely crude Job Activator, designed to create AnnotationsWebJobs and nothing else.
// type AnnotationsWebJobsActivator(connectionString) =
//     interface IJobActivator with
//         member __.CreateInstance<'T>() =
//             AnnotationsWebJobs connectionString |> box :?> 'T

// /// Start up background Azure web jobs with the given Azure connection.
// module WebJobs =
//     open ServerCode.Storage.AzureTable

//     let startWebJobs azureConnection =
//         let host =
//             let config =
//                 let (AzureConnection connectionString) = azureConnection
//                 JobHostConfiguration(
//                     DashboardConnectionString = connectionString,
//                     StorageConnectionString = connectionString)
//             config.UseTimers()
//             config.JobActivator <- AnnotationsWebJobsActivator azureConnection
//             new JobHost(config)
//         host.Start()