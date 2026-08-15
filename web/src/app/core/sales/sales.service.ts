import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AnnexFiveReportDto,
  ApproveCreditNoteResult,
  ApproveInvoiceResult,
  ApproveQuotationResult,
  ApproveSalesOrderResult,
  CreateCreditNoteResult,
  CreateInvoiceResult,
  CreateQuotationResult,
  CreateSalesOrderResult,
  CreditNote,
  CreditNoteConversionTemplate,
  CreditNoteDetail,
  CreditNoteRequest,
  CreditNoteStatus,
  GlLinePreviewDto,
  Invoice,
  InvoiceConversionTemplate,
  InvoiceDetail,
  InvoiceLineInput,
  InvoiceRequest,
  InvoiceStatus,
  Quotation,
  QuotationDetail,
  QuotationRequest,
  QuotationStatus,
  SalesMasterReportDto,
  SalesOrder,
  SalesOrderDetail,
  SalesOrderRequest,
  SalesOrderStatus,
  UpdateCreditNoteResult,
  UpdateInvoiceResult,
  UpdateQuotationResult,
  UpdateSalesOrderResult,
} from './sales.models';

@Injectable({ providedIn: 'root' })
export class SalesService {
  private readonly http = inject(HttpClient);

  private baseUrl(organizationId: string): string {
    return `${environment.apiBaseUrl}/api/organizations/${organizationId}`;
  }

  listQuotations(organizationId: string, status?: QuotationStatus): Observable<Quotation[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<Quotation[]>(`${this.baseUrl(organizationId)}/quotations`, { withCredentials: true, params });
  }

  getQuotation(organizationId: string, id: string): Observable<QuotationDetail> {
    return this.http.get<QuotationDetail>(`${this.baseUrl(organizationId)}/quotations/${id}`, { withCredentials: true });
  }

  createQuotation(organizationId: string, request: QuotationRequest): Observable<CreateQuotationResult> {
    return this.http.post<CreateQuotationResult>(`${this.baseUrl(organizationId)}/quotations`, request, { withCredentials: true });
  }

  updateQuotation(organizationId: string, id: string, request: QuotationRequest): Observable<UpdateQuotationResult> {
    return this.http.put<UpdateQuotationResult>(`${this.baseUrl(organizationId)}/quotations/${id}`, request, {
      withCredentials: true,
    });
  }

  approveQuotation(organizationId: string, id: string): Observable<ApproveQuotationResult> {
    return this.http.post<ApproveQuotationResult>(`${this.baseUrl(organizationId)}/quotations/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  getInvoiceConversionTemplate(organizationId: string, quotationId: string): Observable<InvoiceConversionTemplate> {
    return this.http.get<InvoiceConversionTemplate>(
      `${this.baseUrl(organizationId)}/quotations/${quotationId}/invoice-conversion-template`,
      { withCredentials: true },
    );
  }

  listInvoices(organizationId: string, status?: InvoiceStatus): Observable<Invoice[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<Invoice[]>(`${this.baseUrl(organizationId)}/invoices`, { withCredentials: true, params });
  }

  getInvoice(organizationId: string, id: string): Observable<InvoiceDetail> {
    return this.http.get<InvoiceDetail>(`${this.baseUrl(organizationId)}/invoices/${id}`, { withCredentials: true });
  }

  createInvoice(organizationId: string, request: InvoiceRequest): Observable<CreateInvoiceResult> {
    return this.http.post<CreateInvoiceResult>(`${this.baseUrl(organizationId)}/invoices`, request, { withCredentials: true });
  }

  updateInvoice(organizationId: string, id: string, request: InvoiceRequest): Observable<UpdateInvoiceResult> {
    return this.http.put<UpdateInvoiceResult>(`${this.baseUrl(organizationId)}/invoices/${id}`, request, {
      withCredentials: true,
    });
  }

  approveInvoice(organizationId: string, id: string, overrideWarning = false): Observable<ApproveInvoiceResult> {
    return this.http.post<ApproveInvoiceResult>(
      `${this.baseUrl(organizationId)}/invoices/${id}/approve`, { overrideWarning }, { withCredentials: true },
    );
  }

  previewInvoiceGlPosting(organizationId: string, lines: InvoiceLineInput[]): Observable<GlLinePreviewDto[]> {
    return this.http.post<GlLinePreviewDto[]>(
      `${this.baseUrl(organizationId)}/invoices/preview-gl-posting`,
      { lines },
      { withCredentials: true },
    );
  }

  getCreditNoteConversionTemplate(organizationId: string, invoiceId: string): Observable<CreditNoteConversionTemplate> {
    return this.http.get<CreditNoteConversionTemplate>(
      `${this.baseUrl(organizationId)}/invoices/${invoiceId}/credit-note-conversion-template`,
      { withCredentials: true },
    );
  }

  listSalesOrders(organizationId: string, status?: SalesOrderStatus): Observable<SalesOrder[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<SalesOrder[]>(`${this.baseUrl(organizationId)}/sales-orders`, { withCredentials: true, params });
  }

  getSalesOrder(organizationId: string, id: string): Observable<SalesOrderDetail> {
    return this.http.get<SalesOrderDetail>(`${this.baseUrl(organizationId)}/sales-orders/${id}`, { withCredentials: true });
  }

  createSalesOrder(organizationId: string, request: SalesOrderRequest): Observable<CreateSalesOrderResult> {
    return this.http.post<CreateSalesOrderResult>(`${this.baseUrl(organizationId)}/sales-orders`, request, {
      withCredentials: true,
    });
  }

  updateSalesOrder(organizationId: string, id: string, request: SalesOrderRequest): Observable<UpdateSalesOrderResult> {
    return this.http.put<UpdateSalesOrderResult>(`${this.baseUrl(organizationId)}/sales-orders/${id}`, request, {
      withCredentials: true,
    });
  }

  approveSalesOrder(organizationId: string, id: string): Observable<ApproveSalesOrderResult> {
    return this.http.post<ApproveSalesOrderResult>(`${this.baseUrl(organizationId)}/sales-orders/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  listCreditNotes(organizationId: string, status?: CreditNoteStatus): Observable<CreditNote[]> {
    const params: Record<string, string> = status ? { status } : {};
    return this.http.get<CreditNote[]>(`${this.baseUrl(organizationId)}/credit-notes`, { withCredentials: true, params });
  }

  getCreditNote(organizationId: string, id: string): Observable<CreditNoteDetail> {
    return this.http.get<CreditNoteDetail>(`${this.baseUrl(organizationId)}/credit-notes/${id}`, { withCredentials: true });
  }

  createCreditNote(organizationId: string, request: CreditNoteRequest): Observable<CreateCreditNoteResult> {
    return this.http.post<CreateCreditNoteResult>(`${this.baseUrl(organizationId)}/credit-notes`, request, {
      withCredentials: true,
    });
  }

  updateCreditNote(organizationId: string, id: string, request: CreditNoteRequest): Observable<UpdateCreditNoteResult> {
    return this.http.put<UpdateCreditNoteResult>(`${this.baseUrl(organizationId)}/credit-notes/${id}`, request, {
      withCredentials: true,
    });
  }

  approveCreditNote(organizationId: string, id: string): Observable<ApproveCreditNoteResult> {
    return this.http.post<ApproveCreditNoteResult>(`${this.baseUrl(organizationId)}/credit-notes/${id}/approve`, null, {
      withCredentials: true,
    });
  }

  getSalesMasterReport(
    organizationId: string,
    fromDate: string,
    toDate: string,
    contactId: string | null,
    productId: string | null,
    warehouseId: string | null,
  ): Observable<SalesMasterReportDto> {
    const params: Record<string, string> = { fromDate, toDate };
    if (contactId) params['contactId'] = contactId;
    if (productId) params['productId'] = productId;
    if (warehouseId) params['warehouseId'] = warehouseId;

    return this.http.get<SalesMasterReportDto>(`${this.baseUrl(organizationId)}/reports/sales-master-report`, {
      withCredentials: true,
      params,
    });
  }

  getAnnexFiveReport(organizationId: string, fromDate: string, toDate: string): Observable<AnnexFiveReportDto> {
    const params: Record<string, string> = { fromDate, toDate };
    return this.http.get<AnnexFiveReportDto>(`${this.baseUrl(organizationId)}/reports/annex-five`, {
      withCredentials: true,
      params,
    });
  }
}
