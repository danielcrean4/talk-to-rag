import { Routes } from '@angular/router';
import { signedInGuard } from './auth';
import { GraphPage } from './graph/graph-page';
import { HistoryPage } from './history/history-page';
import { Login } from './login';
import { SearchPage } from './search/search-page';
import { SourcesPage } from './sources/sources-page';

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: '', component: GraphPage, canActivate: [signedInGuard] },
  { path: 'sources', component: SourcesPage, canActivate: [signedInGuard] },
  { path: 'search', component: SearchPage, canActivate: [signedInGuard] },
  { path: 'history', component: HistoryPage, canActivate: [signedInGuard] },
  { path: 'history/:id', component: HistoryPage, canActivate: [signedInGuard] },
  { path: '**', redirectTo: '' },
];
