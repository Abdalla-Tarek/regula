using RegulaWebApp.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
    // ---------------------------------------------
    // ROOT RESPONSE
    // ---------------------------------------------

    /// <summary>
    /// Root response returned by POST /api/process
    /// </summary>
    public class ProcessResponse
    {
        /// <summary>
        /// RFID chip location: 0 = none, 1 = data page, 2 = back page/inlay
        /// </summary>
        [JsonPropertyName("ChipPage")]
        public int ChipPage { get; set; }

        [JsonPropertyName("CoreLibResultCode")]
        public int CoreLibResultCode { get; set; }

        /// <summary>
        /// Processing status: 0 = not finished, 1 = finished, 2 = timeout
        /// </summary>
        [JsonPropertyName("ProcessingFinished")]
        public int ProcessingFinished { get; set; }

        [JsonPropertyName("ContainerList")]
        public ContainerList? ContainerList { get; set; }

        [JsonPropertyName("TransactionInfo")]
        public TransactionInfo? TransactionInfo { get; set; }

        /// <summary>Base64 encoded transaction processing log</summary>
        [JsonPropertyName("log")]
        public string? Log { get; set; }

        /// <summary>Free-form object echoed back from request</summary>
        [JsonPropertyName("passBackObject")]
        public Dictionary<string, object>? PassBackObject { get; set; }

        [JsonPropertyName("morePagesAvailable")]
        public int MorePagesAvailable { get; set; }

        /// <summary>Processing time in milliseconds</summary>
        [JsonPropertyName("elapsedTime")]
        public int ElapsedTime { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    // ---------------------------------------------
    // TRANSACTION INFO
    // ---------------------------------------------

    public class TransactionInfo
    {
        [JsonPropertyName("ComputerName")]
        public string ComputerName { get; set; }

        [JsonPropertyName("DateTime")]
        public DateTime DateTime { get; set; }

        [JsonPropertyName("TransactionID")]
        public string TransactionID { get; set; }

        [JsonPropertyName("UserName")]
        public string UserName { get; set; }
    }

    // ---------------------------------------------
    // CONTAINER LIST
    // ---------------------------------------------

    public class ContainerList
    {
        [JsonPropertyName("Count")]
        public int Count { get; set; }

        [JsonPropertyName("List")]
        public List<ContainerListItem> List { get; set; } = new();
    }

    /// <summary>
    /// A single result container. The shape of the data changes depending on result_type.
    /// Common result types:
    ///   1  = RPRM_ResultType_RawImage
    ///   3  = Status
    ///   6  = MRZ
    ///   9  = Visual / Text fields
    ///   17 = Barcodes
    ///   18 = Graphics / Images
    ///   37 = Document type
    ///   111= Authenticity checks
    /// </summary>
    public class ContainerListItem
    {
        [JsonPropertyName("buf_length")]
        public int BufLength { get; set; }

        /// <summary>Light type: 6 = white, 4 = UV, 2 = IR</summary>
        [JsonPropertyName("light")]
        public int Light { get; set; }

        [JsonPropertyName("list_idx")]
        public int ListIdx { get; set; }

        [JsonPropertyName("page_idx")]
        public int PageIdx { get; set; }

        /// <summary>Result type determines which sub-object is populated</summary>
        [JsonPropertyName("result_type")]
        public int ResultType { get; set; }

        // -- result_type = 3 ------------------------------
        [JsonPropertyName("Status")]
        public StatusResult? Status { get; set; }

        // -- result_type = 9 (text fields) ----------------
        [JsonPropertyName("Text")]
        public TextResult? Text { get; set; }

        // -- result_type = 18 (graphic fields) ------------
        [JsonPropertyName("Images")]
        public ImagesResult? Images { get; set; }

        // -- result_type = 37 (document type) -------------
        [JsonPropertyName("DocType")]
        public List<DocumentType>? DocType { get; set; }

        // -- result_type = 111 (authenticity) -------------
        [JsonPropertyName("Authenticity")]
        public AuthenticityResult? Authenticity { get; set; }

        // -- result_type = 17 (barcodes) ------------------
        [JsonPropertyName("Barcode")]
        public BarcodeResult? Barcode { get; set; }

        // -- result_type = 6 (MRZ) ------------------------
        [JsonPropertyName("MrzOCR")]
        public MrzOcrResult? MrzOCR { get; set; }

        // -- result_type = 1 / raw image -------------------
        [JsonPropertyName("RawImageData")]
        public RawImageData? RawImageData { get; set; }

        // -- Additional sections returned by DocR --------
        [JsonPropertyName("DocVisualExtendedInfo")]
        public DocVisualExtendedInfo? DocVisualExtendedInfo { get; set; }

        [JsonPropertyName("DocGraphicsInfo")]
        public DocGraphicsInfo? DocGraphicsInfo { get; set; }

        [JsonPropertyName("ImageQualityCheckList")]
        public ImageQualityCheckList? ImageQualityCheckList { get; set; }

        [JsonPropertyName("MRZTestQuality")]
        public MrzTestQuality? MRZTestQuality { get; set; }

        [JsonPropertyName("OneCandidate")]
        public OneCandidate? OneCandidate { get; set; }

        [JsonPropertyName("AuthenticityCheckList")]
        public AuthenticityCheckList? AuthenticityCheckList { get; set; }

        [JsonPropertyName("DocumentPosition")]
        public DocumentPositionRaw? DocumentPosition { get; set; }
    }

    // ---------------------------------------------
    // STATUS (result_type = 3)
    // ---------------------------------------------

    /// <summary>
    /// CheckResult values: 0 = OK, 1 = WasNotDone, 2 = Failed
    /// </summary>
    public class StatusResult
    {
        /// <summary>Overall document check result</summary>
        [JsonPropertyName("overallStatus")]
        public int OverallStatus { get; set; }

        /// <summary>Overall optical check result</summary>
        [JsonPropertyName("optical")]
        public int Optical { get; set; }

        /// <summary>Portrait comparison result</summary>
        [JsonPropertyName("portrait")]
        public int Portrait { get; set; }

        /// <summary>RFID chip check result</summary>
        [JsonPropertyName("rfid")]
        public int Rfid { get; set; }

        /// <summary>Stop-list / watchlist check result</summary>
        [JsonPropertyName("stopList")]
        public int StopList { get; set; }

        [JsonPropertyName("detailsRFID")]
        public DetailsRfid DetailsRFID { get; set; }

        [JsonPropertyName("detailsOptical")]
        public DetailsOptical DetailsOptical { get; set; }

        /// <summary>Holder age (years)</summary>
        [JsonPropertyName("age")]
        public int Age { get; set; }

        [JsonPropertyName("detailsAge")]
        public DetailsAge DetailsAge { get; set; }

        [JsonPropertyName("mDL")]
        public int MDL { get; set; }

        [JsonPropertyName("captureProcessIntegrity")]
        public int? CaptureProcessIntegrity { get; set; }
    }

    public class DetailsRfid
    {
        [JsonPropertyName("overallStatus")]
        public int OverallStatus { get; set; }

        /// <summary>Active Authentication</summary>
        [JsonPropertyName("AA")]
        public int AA { get; set; }

        /// <summary>Basic Access Control</summary>
        [JsonPropertyName("BAC")]
        public int BAC { get; set; }

        /// <summary>Chip Authentication</summary>
        [JsonPropertyName("CA")]
        public int CA { get; set; }

        /// <summary>Passive Authentication</summary>
        [JsonPropertyName("PA")]
        public int PA { get; set; }

        /// <summary>Password Authenticated Connection Establishment</summary>
        [JsonPropertyName("PACE")]
        public int PACE { get; set; }

        /// <summary>Terminal Authentication</summary>
        [JsonPropertyName("TA")]
        public int TA { get; set; }
    }

    public class DetailsOptical
    {
        [JsonPropertyName("overallStatus")]
        public int OverallStatus { get; set; }

        /// <summary>Document type identification result</summary>
        [JsonPropertyName("docType")]
        public int DocType { get; set; }

        /// <summary>Expiry date check result</summary>
        [JsonPropertyName("expiry")]
        public int Expiry { get; set; }

        /// <summary>Image quality assessment result</summary>
        [JsonPropertyName("imageQA")]
        public int ImageQA { get; set; }

        /// <summary>MRZ check result</summary>
        [JsonPropertyName("mrz")]
        public int Mrz { get; set; }

        /// <summary>Pages count check result</summary>
        [JsonPropertyName("pagesCount")]
        public int PagesCount { get; set; }

        /// <summary>Security / authenticity checks result</summary>
        [JsonPropertyName("security")]
        public int Security { get; set; }

        /// <summary>Text / OCR fields check result</summary>
        [JsonPropertyName("text")]
        public int Text { get; set; }

        /// <summary>Visible Digital Seal check result</summary>
        [JsonPropertyName("vds")]
        public int Vds { get; set; }
    }

    public class DetailsAge
    {
        [JsonPropertyName("threshold")]
        public int Threshold { get; set; }

        [JsonPropertyName("overThreshold")]
        public int OverThreshold { get; set; }

        [JsonPropertyName("over18")]
        public int Over18 { get; set; }

        [JsonPropertyName("over21")]
        public int Over21 { get; set; }

        [JsonPropertyName("over25")]
        public int Over25 { get; set; }

        [JsonPropertyName("over65")]
        public int Over65 { get; set; }
    }

    // ---------------------------------------------
    // TEXT FIELDS (result_type = 9)
    // ---------------------------------------------

    public class TextResult
    {
        [JsonPropertyName("fieldList")]
        public List<TextField> FieldList { get; set; } = new();

        [JsonPropertyName("availableSourceList")]
        public List<AvailableSource> AvailableSourceList { get; set; } = new();

        /// <summary>Validity status: 0 = OK, 1 = WasNotDone, 2 = Failed</summary>
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("comparisonStatus")]
        public int? ComparisonStatus { get; set; }
    }

    public class TextField
    {
        /// <summary>Field type code (e.g. 0=DocumentNumber, 6=Surname, 8=FirstName, etc.)</summary>
        [JsonPropertyName("fieldType")]
        public int FieldType { get; set; }

        /// <summary>Field name string</summary>
        [JsonPropertyName("fieldName")]
        public string FieldName { get; set; }

        /// <summary>LCID (locale/language identifier)</summary>
        [JsonPropertyName("lcid")]
        public int Lcid { get; set; }

        /// <summary>Validity: 0 = OK, 1 = WasNotDone, 2 = Failed</summary>
        [JsonPropertyName("status")]
        public int Status { get; set; }

        /// <summary>The resolved (most reliable) value for this field</summary>
        [JsonPropertyName("value")]
        public string Value { get; set; }

        /// <summary>Individual source values (MRZ, OCR, barcode, RFID, etc.)</summary>
        [JsonPropertyName("valueList")]
        public List<TextFieldValue> ValueList { get; set; } = new();

        [JsonPropertyName("comparisonList")]
        public List<TextFieldComparison> ComparisonList { get; set; } = new();

        [JsonPropertyName("validityStatus")]
        public int? ValidityStatus { get; set; }

        [JsonPropertyName("comparisonStatus")]
        public int? ComparisonStatus { get; set; }
    }

    public class TextFieldValue
    {
        /// <summary>
        /// Source type: 0=MRZ, 2=Visual/OCR, 3=Barcode, 4=RFID, 5=VisualRFID
        /// </summary>
        [JsonPropertyName("sourceType")]
        public int SourceType { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("originalValue")]
        public string OriginalValue { get; set; }

        /// <summary>Validity: 0=OK, 1=WasNotDone, 2=Failed</summary>
        [JsonPropertyName("validity")]
        public int Validity { get; set; }

        [JsonPropertyName("probability")]
        public int Probability { get; set; }

        [JsonPropertyName("pageIndex")]
        public int PageIndex { get; set; }
    }

    public class TextFieldComparison
    {
        [JsonPropertyName("sourceTypeLeft")]
        public int SourceTypeLeft { get; set; }

        [JsonPropertyName("sourceTypeRight")]
        public int SourceTypeRight { get; set; }

        /// <summary>Comparison result: 0=OK, 1=WasNotDone, 2=Failed</summary>
        [JsonPropertyName("status")]
        public int Status { get; set; }
    }

    public class AvailableSource
    {
        [JsonPropertyName("sourceType")]
        public int SourceType { get; set; }

        [JsonPropertyName("validityStatus")]
        public int ValidityStatus { get; set; }
    }

    // ---------------------------------------------
    // IMAGES / GRAPHICS (result_type = 18)
    // ---------------------------------------------

    public class ImagesResult
    {
        [JsonPropertyName("fieldList")]
        public List<ImageField> FieldList { get; set; } = new();

        [JsonPropertyName("availableSourceList")]
        public List<AvailableSource> AvailableSourceList { get; set; } = new();

        [JsonPropertyName("status")]
        public int Status { get; set; }
    }

    public class ImageField
    {
        /// <summary>
        /// Graphic field type:
        ///   201 = Portrait, 202 = Signature, 203 = Fingerprint,
        ///   204 = Ghost portrait, 205 = Stamp, 250 = Document image
        /// </summary>
        [JsonPropertyName("fieldType")]
        public int FieldType { get; set; }

        [JsonPropertyName("fieldName")]
        public string FieldName { get; set; }

        [JsonPropertyName("valueList")]
        public List<ImageFieldValue> ValueList { get; set; } = new();
    }

    public class ImageFieldValue
    {
        /// <summary>Source type: 0=MRZ, 2=Visual, 3=Barcode, 4=RFID</summary>
        [JsonPropertyName("sourceType")]
        public int SourceType { get; set; }

        /// <summary>Base64 encoded image</summary>
        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("originalValue")]
        public string OriginalValue { get; set; }

        [JsonPropertyName("pageIndex")]
        public int PageIndex { get; set; }

        [JsonPropertyName("lightIndex")]
        public int LightIndex { get; set; }

        [JsonPropertyName("validity")]
        public int Validity { get; set; }
    }

    // ---------------------------------------------
    // DOCUMENT TYPE (result_type = 37)
    // ---------------------------------------------

    public class DocumentType
    {
        [JsonPropertyName("pageIndex")]
        public int PageIndex { get; set; }

        [JsonPropertyName("DocumentID")]
        public int DocumentID { get; set; }

        [JsonPropertyName("dType")]
        public int DType { get; set; }

        [JsonPropertyName("dFormat")]
        public int DFormat { get; set; }

        [JsonPropertyName("dMRZ")]
        public bool DMRZ { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("ICAOCode")]
        public string ICAOCode { get; set; }

        [JsonPropertyName("IssuingState")]
        public string IssuingState { get; set; }

        [JsonPropertyName("IssuingStateName")]
        public string IssuingStateName { get; set; }

        [JsonPropertyName("PossibleDocumentIdentifiers")]
        public List<PossibleDocumentIdentifier> PossibleDocumentIdentifiers { get; set; } = new();
    }

    public class PossibleDocumentIdentifier
    {
        [JsonPropertyName("DocumentID")]
        public int DocumentID { get; set; }

        [JsonPropertyName("Probability")]
        public int Probability { get; set; }
    }

    // ---------------------------------------------
    // AUTHENTICITY (result_type = 111)
    // ---------------------------------------------

    public class AuthenticityResult
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("CheckList")]
        public List<AuthenticityCheck> CheckList { get; set; } = new();
    }

    public class AuthenticityCheck
    {
        /// <summary>
        /// Check type codes (examples):
        ///   0  = UV Luminescence
        ///   1  = Image Pattern
        ///   2  = UV Fibers
        ///   4  = IR Visibility
        ///   5  = IPI
        ///   7  = Photo Embedding
        ///   8  = Extended MRZ
        ///   9  = Extended OCR
        ///   10 = Liveness (OVI/Holo/MLI/etc.)
        ///   11 = IRB900
        ///   13 = Portrait Comparison
        ///   17 = Security Text
        ///   21 = Axial Protection
        ///   22 = Barcode Format
        ///   25 = Encrypted IPI
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; }

        /// <summary>Check result: 0=OK, 1=WasNotDone, 2=Failed</summary>
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("TypicalName")]
        public string TypicalName { get; set; }

        [JsonPropertyName("ElementList")]
        public List<AuthenticityElement> ElementList { get; set; } = new();
    }

    public class AuthenticityElement
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("ElementDiagnose")]
        public int ElementDiagnose { get; set; }

        [JsonPropertyName("ElementResult")]
        public int? ElementResult { get; set; }

        [JsonPropertyName("ElementType")]
        public int ElementType { get; set; }

        [JsonPropertyName("RectArray")]
        public List<RectCoordinates> RectArray { get; set; } = new();

        /// <summary>Base64 encoded reference image (for image pattern checks)</summary>
        [JsonPropertyName("etalonImage")]
        public string EtalonImage { get; set; }

        /// <summary>Base64 encoded actual image from document</summary>
        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("lightIndex")]
        public int LightIndex { get; set; }

        [JsonPropertyName("pageIndex")]
        public int PageIndex { get; set; }

        [JsonPropertyName("colorValues")]
        public List<int> ColorValues { get; set; } = new();
    }

    public class RectCoordinates
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    // ---------------------------------------------
    // BARCODES (result_type = 17)
    // ---------------------------------------------

    public class BarcodeResult
    {
        [JsonPropertyName("List")]
        public List<BarcodeItem> List { get; set; } = new();
    }

    public class BarcodeItem
    {
        /// <summary>Barcode type (e.g. PDF417, QR, Aztec, Code128, etc.)</summary>
        [JsonPropertyName("barcodeType")]
        public int BarcodeType { get; set; }

        /// <summary>Decoded string value</summary>
        [JsonPropertyName("decodedData")]
        public string DecodedData { get; set; }

        /// <summary>Raw bytes as base64</summary>
        [JsonPropertyName("data")]
        public string Data { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("pageIndex")]
        public int PageIndex { get; set; }

        [JsonPropertyName("pdf417Info")]
        public Pdf417Info Pdf417Info { get; set; }

        [JsonPropertyName("fieldList")]
        public List<TextField> FieldList { get; set; } = new();
    }

    public class Pdf417Info
    {
        [JsonPropertyName("errorLevel")]
        public int ErrorLevel { get; set; }

        [JsonPropertyName("columns")]
        public int Columns { get; set; }

        [JsonPropertyName("rows")]
        public int Rows { get; set; }
    }

    // ---------------------------------------------
    // MRZ (result_type = 6)
    // ---------------------------------------------

    public class MrzOcrResult
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("fields")]
        public List<TextField> Fields { get; set; } = new();

        [JsonPropertyName("mrzText")]
        public string MrzText { get; set; }
    }

    // ---------------------------------------------
    // RAW IMAGE (result_type = 1)
    // ---------------------------------------------

    public class RawImageData
    {
        /// <summary>Base64 encoded image</summary>
        [JsonPropertyName("image")]
        public string Image { get; set; }

        [JsonPropertyName("pageIndex")]
        public int PageIndex { get; set; }

        [JsonPropertyName("lightIndex")]
        public int LightIndex { get; set; }
    }

    // ---------------------------------------------
    // EXTRA DOCV / QUALITY / CANDIDATE SECTIONS
    // ---------------------------------------------

    public class DocVisualExtendedInfo
    {
        [JsonPropertyName("pArrayFields")]
        public List<DocVisualField> PArrayFields { get; set; } = new();
    }

    public class DocVisualField
    {
        [JsonPropertyName("FieldName")]
        public string? FieldName { get; set; }

        [JsonPropertyName("Buf_Text")]
        public string? BufText { get; set; }
    }

    public class DocGraphicsInfo
    {
        [JsonPropertyName("pArrayFields")]
        public List<DocGraphicField> PArrayFields { get; set; } = new();
    }

    public class DocGraphicField
    {
        [JsonPropertyName("FieldName")]
        public string? FieldName { get; set; }

        [JsonPropertyName("image")]
        public JsonElement? Image { get; set; }
    }

    public class ImageQualityCheckList
    {
        [JsonPropertyName("List")]
        public List<ImageQualityCheck> List { get; set; } = new();
    }

    public class ImageQualityCheck
    {
        [JsonPropertyName("result")]
        public int? Result { get; set; }

        [JsonPropertyName("type")]
        public int? Type { get; set; }

        [JsonPropertyName("probability")]
        public int? Probability { get; set; }
    }

    public class MrzTestQuality
    {
        [JsonPropertyName("CHECK_SUMS")]
        public int? CheckSums { get; set; }
    }

    public class OneCandidate
    {
        [JsonPropertyName("DocumentName")]
        public string? DocumentName { get; set; }

        [JsonPropertyName("FDSIDList")]
        public FdsIdList? FDSIDList { get; set; }
    }

    public class FdsIdList
    {
        [JsonPropertyName("Count")]
        public int? Count { get; set; }

        [JsonPropertyName("ICAOCode")]
        public string? ICAOCode { get; set; }
    }

    public class AuthenticityCheckList
    {
        [JsonPropertyName("List")]
        public List<AuthenticityCheckGroup> List { get; set; } = new();
    }

    public class AuthenticityCheckGroup
    {
        [JsonPropertyName("List")]
        public List<AuthenticityCheckElement> List { get; set; } = new();
    }

    public class AuthenticityCheckElement
    {
        [JsonPropertyName("Type")]
        public int? Type { get; set; }

        [JsonPropertyName("ElementType")]
        public int? ElementType { get; set; }

        [JsonPropertyName("ElementDiagnose")]
        public int? ElementDiagnose { get; set; }

        [JsonPropertyName("ElementResult")]
        public int? ElementResult { get; set; }
    }

    // ---------------------------------------------
    // ENUMS FOR READABILITY (optional helpers)
    // ---------------------------------------------

    public static class CheckResult
    {
        public const int OK = 0;
        public const int WasNotDone = 1;
        public const int Failed = 2;
    }

    public static class ProcessingStatus
    {
        public const int NotFinished = 0;
        public const int Finished = 1;
        public const int Timeout = 2;
    }

    public static class RfidLocation
    {
        public const int None = 0;
        public const int DataPage = 1;
        public const int BackPage = 2;
    }

    public static class LightType
    {
        public const int IR = 2;
        public const int UV = 4;
        public const int White = 6;
    }

    public static class SourceType
    {
        public const int MRZ = 0;
        public const int Visual = 2;
        public const int Barcode = 3;
        public const int RFID = 4;
        public const int LivePhoto = 5;
    }

    public static class AuthCheckType
    {
        public const int UVLuminescence = 0;
        public const int ImagePattern = 1;
        public const int UVFibers = 2;
        public const int IRVisibility = 4;
        public const int IPI = 5;
        public const int PhotoEmbedding = 7;
        public const int ExtendedMRZ = 8;
        public const int ExtendedOCR = 9;
        public const int Liveness = 10;
        public const int IRB900 = 11;
        public const int PortraitCompare = 13;
        public const int SecurityText = 17;
        public const int AxialProtection = 21;
        public const int BarcodeFormat = 22;
        public const int EncryptedIPI = 25;
    }

    public static class ResultType
    {
        public const int RawImage = 1;
        public const int Status = 3;
        public const int MrzOcr = 6;
        public const int VisualOcr = 9;
        public const int Barcodes = 17;
        public const int Graphics = 18;
        public const int DocumentType = 37;
        public const int Authenticity = 111;
    }

    public static class GraphicFieldType
    {
        public const int Portrait = 201;
        public const int Signature = 202;
        public const int Fingerprint = 203;
        public const int GhostPortrait = 204;
        public const int Stamp = 205;
        public const int DocumentFront = 250;
        public const int DocumentBack = 251;
    }

