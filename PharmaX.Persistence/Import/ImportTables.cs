using System.Text.RegularExpressions;

namespace PharmaX.Persistence.Import;

/// <summary>What a table holds, as far as the import is concerned.</summary>
internal enum TableKind
{
    Departments, Suppliers, Products, Stock, Patients, Prescribers,
    Prescriptions, SaleLines, Receipts, StockMovements, StockCounts,
    PriceChanges, PriceOverrides, CashUps, ZReads,

    /// The exporting system's own totals, taken as it read the data.
    Controls,

    /// Stock per branch: PharmaX holds one figure per product.
    BranchStock,

    /// Totals worked out from other tables - by day, by month, top tens.
    /// PharmaX's reports work them out again from what is imported.
    Summary,

    Unknown
}

/// <summary>
/// The names each field goes by, in <see cref="SourceTable.Key"/> form:
/// the old system's own first, then the names other systems and
/// spreadsheets commonly use.
/// </summary>
internal static class F
{
    // Shared.
    public static readonly string[] ProductCode = { "productcode", "itemcode", "stockcode", "sku", "productid", "itemno", "code" };
    public static readonly string[] ProductName = { "productname", "itemname", "productdescription", "itemdescription", "description", "product", "item", "name" };
    public static readonly string[] Barcode = { "barcode", "ean", "gtin", "upc", "ean13" };
    public static readonly string[] Quantity = { "quantity", "qty", "qtyonhand", "quantityonhand", "onhand", "stockonhand", "soh", "units" };
    public static readonly string[] PackSize = { "packsize", "unitsperpack", "pack", "packqty" };
    public static readonly string[] BatchNo = { "batchno", "batchnumber", "batch", "lotno", "lotnumber", "lot" };
    public static readonly string[] BatchExpiry = { "batchexpiry", "expirydate", "expiry", "expires", "bestbefore" };
    public static readonly string[] CostPrice = { "costprice", "unitcost", "cost" };
    public static readonly string[] SellingPrice = { "sellingprice", "retailprice", "sellprice", "unitprice", "price" };
    public static readonly string[] Operator = { "operator", "operatorname", "cashier", "username", "user", "capturedby" };
    public static readonly string[] Supplier = { "supplier", "suppliername", "suppliercode", "vendor" };
    public static readonly string[] Department = { "department", "departmentname", "deptname", "deptid", "departmentid" };
    public static readonly string[] Strength = { "strength" };
    public static readonly string[] Tender = { "tender", "tendertype", "paymenttype", "paymentmethod" };
    public static readonly string[] Rate = { "tenderexchange", "exchangerate", "forexexchangerate", "rate" };

    // Departments.
    public static readonly string[] DeptId = { "deptid", "departmentid", "deptcode", "departmentcode", "code", "id" };
    public static readonly string[] DeptName = { "deptname", "departmentname", "name", "description" };

    // Suppliers.
    public static readonly string[] SupplierCode = { "suppliercode", "supplierid", "vendorcode", "creditorcode", "code", "id" };
    public static readonly string[] SupplierName = { "suppliername", "vendorname", "creditorname", "name" };
    public static readonly string[] Telephone = { "telephone", "phone", "tel", "phonenumber", "cellphone", "mobile" };
    public static readonly string[] Email = { "emailaddress", "email" };
    public static readonly string[] Contact = { "contactname", "contact", "contactperson" };
    public static readonly string[] Account = { "accountnumber", "accountno", "ouraccount" };

    // Products.
    public static readonly string[] GenericName = { "altproductname", "genericname", "generic", "activeingredient" };
    public static readonly string[] Form = { "formcode", "form", "dosageform" };
    public static readonly string[] Unit = { "unitofmeasure", "uom", "unit" };
    public static readonly string[] Schedule = { "drugschedule", "schedule", "scheduling" };
    public static readonly string[] TaxCode = { "taxcode", "vatcode", "vat", "vatrate", "taxrate" };
    public static readonly string[] ReorderLevel = { "reorderlevel", "reorderpoint", "minimumstock", "minstock" };
    public static readonly string[] Discontinued = { "discontinuedon", "discontinued", "inactive" };
    public static readonly string[] Changed = { "timetrans", "lastupdated", "modified", "createdon", "created", "datecreated" };

    // Patients.
    public static readonly string[] Member = { "membercode", "memberno", "membernumber", "fileno", "filenumber", "patientno", "patientnumber", "patientid", "accountno", "code", "id" };
    public static readonly string[] Dependant = { "dependentid", "dependantid", "dependent", "dependant", "depno" };
    public static readonly string[] DependantCode = { "dependentsuffix", "dependantsuffix", "dependantcode", "dependentcode" };
    public static readonly string[] FirstName = { "firstname", "firstnames", "forename", "forenames", "givenname", "givennames" };
    public static readonly string[] Surname = { "surname", "lastname", "familyname" };
    public static readonly string[] FullName = { "patientname", "fullname", "patient", "name" };
    public static readonly string[] Title = { "title" };
    public static readonly string[] Initials = { "initials" };
    public static readonly string[] BirthDate = { "dateofbirth", "dob", "birthdate", "birthday" };
    public static readonly string[] IdNumber = { "idno", "idnumber", "nationalid", "nationalidnumber" };
    public static readonly string[] Cellphone = { "cellphone", "cell", "mobile", "mobilephone" };
    public static readonly string[] Phone = { "phone", "phone1", "telephone", "tel" };
    public static readonly string[] Landline = { "tel", "phone2", "homephone", "workphone" };
    public static readonly string[] Street = { "street", "supplierstreet", "address1", "streetaddress" };
    public static readonly string[] Area = { "area", "supplierarea", "suburb", "address2" };
    public static readonly string[] City = { "city", "suppliertown", "town" };
    public static readonly string[] Address = { "address", "postaladdress", "physicaladdress" };
    public static readonly string[] MedicalAid = { "medicalaid", "medicalaidscheme", "scheme", "medicalaidname" };
    public static readonly string[] Membership = { "claimnumber", "membershipnumber", "medicalaidno", "medicalaidnumber", "membershipno" };
    public static readonly string[] Allergies = { "allergies", "allergy" };
    public static readonly string[] Conditions = { "conditions", "chronicconditions" };
    public static readonly string[] Owing = { "unpaidscripts", "balance", "owing", "balancedue" };

    // Prescribers.
    public static readonly string[] DoctorCode = { "doctorcode", "prescribercode", "doctorid", "prescriberid", "code", "id" };
    public static readonly string[] DoctorName = { "doctorname", "prescribername", "doctor", "prescriber", "name" };
    public static readonly string[] PracticeNo = { "practiceno", "practicenumber", "practice" };

    // Prescriptions.
    public static readonly string[] ScriptNo = { "scriptno", "scriptnumber", "prescriptionno", "prescriptionnumber", "rxno", "rxnumber" };
    public static readonly string[] ScriptDate = { "dispensedate", "dispenseddate", "prescriptiondate", "scriptdate", "date" };
    public static readonly string[] ScriptTime = { "dispensetime", "time" };
    public static readonly string[] Directions = { "dosage", "directions", "instructions", "sig" };
    public static readonly string[] Prescriber = { "doctor", "prescriber", "doctorname", "prescribername" };
    public static readonly string[] Pharmacist = { "pharmacist", "dispensedby" };
    public static readonly string[] Repeats = { "repeats", "repeatsauthorised", "repeatsallowed" };
    public static readonly string[] RepeatsLeft = { "repeatsleft", "repeatsremaining" };
    public static readonly string[] Fee = { "dispensingfee", "professionalfee", "fee" };
    public static readonly string[] LinePrice = { "sellingprice", "lineprice", "price", "amount" };
    public static readonly string[] SchemePaid = { "posmedaidpaid", "medaidpaid", "medicalaidpaid", "schemepaid" };
    public static readonly string[] PatientPaid = { "posamountpaid", "amountpaid", "patientpaid", "paid" };
    public static readonly string[] Deleted = { "deleted", "isdeleted", "cancelled", "voided" };

    // Till.
    public static readonly string[] TransNo = { "transno", "transactionno", "transactionnumber", "receiptno", "receiptnumber", "saleno" };
    public static readonly string[] SaleDate = { "saledate", "transdate", "transactiondate", "date" };
    public static readonly string[] SaleTime = { "saletime", "transtime", "time" };
    public static readonly string[] Till = { "tillno", "till", "tillnumber", "register" };
    public static readonly string[] LineType = { "type", "transtype", "linetype" };
    public static readonly string[] Discount = { "discountamount", "discount" };
    public static readonly string[] LineTotal = { "linetotal", "lineamount", "total", "amount", "value" };
    public static readonly string[] ReceiptTotal = { "transtotal", "receipttotal", "total", "amount" };
    public static readonly string[] Tendered = { "amounttendered", "tendered" };
    public static readonly string[] Change = { "changeamount", "change", "changegiven" };
    public static readonly string[] TenderRef = { "tenderref", "paymentreference", "reference" };
    public static readonly string[] ReturnReason = { "returnreason", "reason" };

    // Stock movements.
    public static readonly string[] MoveDate = { "movedate", "movementdate", "receiveddate", "date" };
    public static readonly string[] MoveType = { "type", "movementtype", "transtype" };
    public static readonly string[] Category = { "category" };
    public static readonly string[] GrNo = { "grno", "grnno", "grn", "goodsreceivedno", "deliveryno" };
    public static readonly string[] InvoiceNo = { "invoiceno", "invoicenumber", "supplierinvoice" };
    public static readonly string[] DocNo = { "docno", "documentno", "documentnumber" };
    public static readonly string[] Comment = { "comment", "comments", "notes", "reason" };

    // Price changes and overrides.
    public static readonly string[] ChangeDate = { "changedate", "date" };
    public static readonly string[] ChangeTime = { "changetime", "time" };
    public static readonly string[] OldCost = { "oldcost" };
    public static readonly string[] NewCost = { "newcost" };
    public static readonly string[] OldPrice = { "oldselling", "oldprice", "oldsellingprice" };
    public static readonly string[] NewPrice = { "newselling", "newprice", "newsellingprice" };
    public static readonly string[] OverrideDate = { "overridedate", "date" };
    public static readonly string[] OverrideTime = { "overridetime", "time" };
    public static readonly string[] ChargedAs = { "changedto", "overrideprice", "chargedprice" };
    public static readonly string[] AuthorisedBy = { "authorizedbyname", "authorisedbyname", "authorizedby", "authorisedby" };

    // Counts.
    public static readonly string[] CountDate = { "countdate", "stocktakedate", "date" };
    public static readonly string[] CountId = { "cyclecountid", "countid", "stocktakeid", "stocktakeno" };
    public static readonly string[] Expected = { "priorqty", "expectedqty", "systemqty", "expected", "onrecord" };
    public static readonly string[] Counted = { "actualqty", "countedqty", "counted", "actual" };

    // Cash-ups.
    public static readonly string[] ZRead = { "zreadid", "zread", "zreadno", "sessionno", "sessionid" };
    public static readonly string[] CashUpDate = { "forday", "date", "transdate" };
    public static readonly string[] SystemTotal = { "systemtotal", "expected", "expectedtotal" };
    public static readonly string[] Declared = { "actualtotal", "declared", "declaredtotal", "counted" };
    public static readonly string[] FromReceipt = { "transfrom", "fromreceipt", "firstreceipt" };
    public static readonly string[] ToReceipt = { "transto", "toreceipt", "lastreceipt" };

    // Controls and branch stock.
    public static readonly string[] Control = { "control" };
    public static readonly string[] ControlValue = { "database", "value", "expected" };
    public static readonly string[] Branch = { "branchno", "branch", "branchcode", "branchid" };

    /// <summary>
    /// Names too common to say on their own what a table is: "Code" and
    /// "Name" are a department list, a supplier list or a product list.
    /// A table recognised by these alone needs its name to agree.
    /// </summary>
    public static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "code", "id", "name", "description", "date", "time", "total", "amount", "quantity",
        "qty", "units", "type", "value", "price", "cost", "product", "item", "patient",
        "doctor", "prescriber", "supplier", "department", "operator", "user", "reference",
        "notes", "comment", "reason", "batch", "expiry", "phone", "tel", "telephone", "title",
        "expected", "counted", "actual", "pack", "rate", "category", "scheme", "fee", "paid",
        "practice", "unit", "form"
    };
}

/// <summary>
/// Tells what a table holds from its columns, with its name as the
/// tie-breaker - so a sheet called "Sheet1" with a product code and a
/// name is still a product list, and "Daily_By_Tender" is still a summary
/// however much it looks like the receipts it was totalled from.
/// </summary>
internal static partial class ImportTables
{
    private sealed record Spec(TableKind Kind, string[] Hints, string[][] Required,
                               string[][] Optional, string[][]? Without = null);

    private static readonly Spec[] Specs =
    {
        new(TableKind.Departments, new[] { "depart", "dept" },
            new[] { F.DeptName }, new[] { F.DeptId }),

        new(TableKind.Suppliers, new[] { "supplier", "vendor", "creditor", "wholesaler" },
            new[] { F.SupplierCode, F.SupplierName },
            new[] { F.Telephone, F.Email, F.Street, F.City, F.Contact }),

        new(TableKind.Products, new[] { "product", "item", "catalog", "inventor", "drug", "medicine" },
            new[] { F.ProductCode, F.ProductName },
            new[] { F.Barcode, F.GenericName, F.Form, F.Strength, F.Department, F.Supplier,
                    F.Schedule, F.TaxCode, F.Unit, F.ReorderLevel, F.Discontinued,
                    F.SellingPrice, F.CostPrice, F.Quantity, F.PackSize }),

        // Stock with nothing to say what the product is called: the
        // shelf's own table, joined to the catalogue by its code.
        new(TableKind.Stock, new[] { "stock", "onhand", "soh" },
            new[] { F.ProductCode, F.Quantity },
            new[] { F.PackSize, F.BatchNo, F.BatchExpiry, F.CostPrice, F.SellingPrice },
            Without: new[] { F.ProductName, F.Branch }),

        new(TableKind.BranchStock, new[] { "branch" },
            new[] { F.Branch, F.ProductCode, F.Quantity }, Array.Empty<string[]>()),

        new(TableKind.Patients, new[] { "patient", "client", "customer", "member" },
            new[] { F.Surname, F.FirstName },
            new[] { F.Member, F.Dependant, F.BirthDate, F.IdNumber, F.Phone, F.Cellphone,
                    F.Street, F.City, F.MedicalAid, F.Membership, F.Allergies },
            Without: new[] { F.DoctorCode.Take(4).ToArray(), F.PracticeNo }),

        new(TableKind.Patients, new[] { "patient", "client", "customer" },
            new[] { F.FullName }, new[] { F.Member, F.BirthDate, F.IdNumber, F.Phone },
            Without: new[] { F.ScriptNo, F.ProductCode, F.TransNo }),

        new(TableKind.Prescribers, new[] { "doctor", "prescriber", "physician", "practitioner" },
            new[] { F.DoctorCode, F.Surname },
            new[] { F.Title, F.Initials, F.FirstName, F.PracticeNo, F.Phone, F.City }),

        new(TableKind.Prescribers, new[] { "doctor", "prescriber", "physician", "practitioner" },
            new[] { F.DoctorCode, F.DoctorName }, new[] { F.PracticeNo, F.Phone }),

        new(TableKind.Prescriptions, new[] { "prescription", "script", "dispens", "rx" },
            new[] { F.ScriptNo, F.ProductCode, F.Quantity },
            new[] { F.ScriptDate, F.Directions, F.Prescriber, F.Pharmacist, F.Member,
                    F.LinePrice, F.Fee, F.PatientPaid, F.Deleted }),

        new(TableKind.SaleLines, new[] { "sale", "till", "pos", "transaction", "invoice" },
            new[] { F.TransNo, F.ProductCode, F.Quantity, F.LineTotal },
            new[] { F.SaleDate, F.SaleTime, F.Operator, F.Tender, F.SellingPrice,
                    F.Discount, F.PackSize, F.LineType }),

        new(TableKind.Receipts, new[] { "receipt", "sale", "till", "pos", "transaction" },
            new[] { F.TransNo, F.ReceiptTotal, F.SaleDate },
            new[] { F.SaleTime, F.Operator, F.Tender, F.Tendered, F.Change, F.Rate },
            Without: new[] { F.ProductCode }),

        new(TableKind.StockMovements, new[] { "movement", "stock", "receiv", "grn", "purchase", "journal" },
            new[] { F.MoveDate, F.MoveType, F.ProductCode, F.Quantity },
            new[] { F.GrNo, F.InvoiceNo, F.Supplier, F.BatchNo, F.CostPrice, F.LineTotal, F.Category },
            Without: new[] { F.TransNo }),

        new(TableKind.StockCounts, new[] { "count", "stocktake" },
            new[] { F.ProductCode, F.Expected, F.Counted },
            new[] { F.CountDate, F.CountId, F.PackSize, F.CostPrice, F.Operator }),

        new(TableKind.PriceChanges, new[] { "price" },
            new[] { F.ProductCode, F.NewPrice, F.ChangeDate },
            new[] { F.ChangeTime, F.OldPrice, F.OldCost, F.NewCost, F.Operator }),

        new(TableKind.PriceOverrides, new[] { "override" },
            new[] { F.ProductCode, F.ChargedAs, F.OverrideDate },
            new[] { F.OverrideTime, F.SellingPrice, F.Quantity, F.Operator, F.AuthorisedBy }),

        new(TableKind.CashUps, new[] { "dayend", "cashup", "declaration", "zread" },
            new[] { F.ZRead, F.Tender, F.SystemTotal, F.Declared },
            new[] { F.CashUpDate, F.Operator, F.Rate }),

        new(TableKind.ZReads, new[] { "zread", "session", "shift" },
            new[] { F.ZRead, F.FromReceipt, F.ToReceipt },
            new[] { F.CashUpDate, F.Operator, F.AuthorisedBy }),

        new(TableKind.Controls, new[] { "control" },
            new[] { F.Control, F.ControlValue }, Array.Empty<string[]>())
    };

    /// <summary>
    /// What the table holds: every required field present, and either a
    /// field that could only mean this or a name that says so. Where more
    /// than one kind fits, the one that asks the most of the table wins -
    /// a till's sale lines have a product code and a name too, but they
    /// are not a product list.
    /// </summary>
    public static TableKind Recognise(SourceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var name = SourceTable.Key(table.Name);

        if (SummaryName().IsMatch(table.Name)) return TableKind.Summary;

        var best = (Kind: TableKind.Unknown, Score: 0);

        foreach (var spec in Specs)
        {
            if (!spec.Required.All(table.Has)) continue;
            if (spec.Without?.Any(table.Has) == true) continue;

            var hinted = spec.Hints.Any(h => name.Contains(h, StringComparison.Ordinal));
            var specific = spec.Required.Any(field =>
                field.FirstOrDefault(alias => table.Has(new[] { alias })) is string used
                && !F.Generic.Contains(used));

            if (!hinted && !specific) continue;

            var score = spec.Required.Length * 10
                        + spec.Optional.Count(table.Has)
                        + (hinted ? 5 : 0);

            if (score > best.Score) best = (spec.Kind, score);
        }

        return best.Kind;
    }

    /// "Daily_By_Tender", "Monthly_Dispensing", "Top_Dispensed_Products",
    /// "Scripts_Summary": totals, not records.
    [GeneratedRegex(@"(?i)(summary|^top[\s_\-]|monthly|weekly|daily|(^|[\s_\-])by[\s_\-])")]
    private static partial Regex SummaryName();
}
