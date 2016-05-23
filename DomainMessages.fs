module DomainMessages

type DomainMessage =
    | Request of string
    | Reply of string
