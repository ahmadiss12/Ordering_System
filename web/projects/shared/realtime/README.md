# realtime

The live channel between the API's orders hub and a screen that has to stay current.

One service, `OrderStream`. A screen watches its `revision` signal and refetches whenever it
changes; that one signal covers both a pushed message and the poll that stands behind it, so a
screen never has two code paths for the same job. See `order-stream.ts` for why it is shaped that
way.
