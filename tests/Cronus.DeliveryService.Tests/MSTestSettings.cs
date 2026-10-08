// The tests share one PostgreSQL database and reset it between runs, so they must not execute
// concurrently. This is deliberate rather than a default: enabling parallelism would require every
// test to own a private database.
[assembly: DoNotParallelize]
