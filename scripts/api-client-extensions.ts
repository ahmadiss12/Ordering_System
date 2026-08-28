/**
 * Injected into the generated client by NSwag.
 *
 * NSwag references FileParameter for multipart uploads but does not emit the type itself, so
 * the generated file would not compile without this. Declaring it here keeps the fix inside the
 * generation pipeline rather than as a hand edit somebody would overwrite on the next run.
 */
export interface FileParameter {
    data: Blob;
    fileName: string;
}
